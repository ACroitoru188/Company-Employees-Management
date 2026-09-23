using Blazored.LocalStorage;
using CompanyEmployees.Application;
using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Gateway;
using CompanyEmployees.Infrastructure;
using CompanyEmployees.Infrastructure.ExceptionHandling;
using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Web.Components;
using CompanyEmployees.Web.Endpoints;
using CompanyEmployees.Web.Extensions;
using CompanyEmployees.Web.Plugins;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using CompanyEmployees.Web.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var startupSw = System.Diagnostics.Stopwatch.StartNew();
    var builder = WebApplication.CreateBuilder(args);

    long failoverSelectMs = 0;

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Provider discovery via ProviderLoader
    // Bootstrap a minimal logger so ProviderLoader can report issues during startup (before the full DI container is built).
    using var bootstrapFactory = LoggerFactory.Create(b => b.AddSerilog(Log.Logger));
    var bootstrapLogger = bootstrapFactory.CreateLogger("Startup");

    var plugins = ProviderLoader.Load(builder.Environment.ContentRootPath, bootstrapLogger);
    var catalog = new DatabaseProviderCatalog(plugins, builder.Configuration);

    // Load setup state from App_Data/setup-state.json
    var setupStore = new JsonSetupStateStore(builder.Environment);
    var setupState = setupStore.Load();
    var benchmarkStore = new ProviderBenchmarkStore(builder.Environment);

    IDbProviderPlugin? primaryPlugin = null;
    IDbProviderPlugin? secondaryPlugin = null;
    DatabaseRuntimeState? databaseState = null;
    string primaryConnectionString = string.Empty;
    string? secondaryConnectionString = null;

    if (setupState.IsComplete)
    {
        var primaryProviderId = setupState.PrimaryProviderId ?? "sqlserver";
        primaryConnectionString = setupState.PrimaryConnectionString
            ?? builder.Configuration.GetConnectionString("Default") ?? string.Empty;
        var secondaryProviderId = setupState.SecondaryProviderId;
        secondaryConnectionString = setupState.SecondaryConnectionString;

        primaryPlugin = catalog.FindById(primaryProviderId)
            ?? plugins.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Primary database provider '{primaryProviderId}' could not be found. " +
                "Ensure the Providers/ folder contains the correct plugin DLL.");
        secondaryPlugin = string.IsNullOrEmpty(secondaryConnectionString)
            ? null
            : catalog.FindById(secondaryProviderId);

        var swFailover = System.Diagnostics.Stopwatch.StartNew();
        databaseState = await DatabaseFailoverSelector.SelectAsync(
            primaryPlugin: primaryPlugin,
            primaryConnectionString: primaryConnectionString,
            secondaryPlugin: secondaryPlugin,
            secondaryConnectionString: secondaryConnectionString,
            configuration: builder.Configuration);
        failoverSelectMs = swFailover.ElapsedMilliseconds;
    }

    // Persist Data Protection keys to a configurable directory
    builder.Services.AddAppDataProtection(builder.Configuration, builder.Environment);

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
            options.DetailedErrors = builder.Environment.IsDevelopment());
    builder.Services.AddFluentUIComponents();
    builder.Services.AddBlazoredLocalStorage();
    builder.Services.AddScoped<ThemeState>();
    // Scoped: the Team page and the layout's chat panel share one instance per circuit.
    builder.Services.AddScoped<ChatPanelState>();
    builder.Services.AddScoped<CircuitCulture>();

    builder.Services.AddSingleton(catalog);
    builder.Services.AddSingleton<ISetupStateStore>(setupStore);
    builder.Services.AddSingleton(benchmarkStore);
    builder.Services.AddSingleton<AppLocalizer>();
    builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
    builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
    builder.Services.AddControllers();
    builder.Services.AddSignalR(options =>
    {
        options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
    });

    if (setupState.IsComplete)
    {
        builder.Services.AddScoped<EmployeeAccountService>();
        builder.Services.AddScoped<ActingContext>();
        builder.Services.AddScoped<EmployeeCsvExportService>();
        builder.Services.AddScoped<LanguagePreferenceService>();
        builder.Services.AddScoped<ITimeOffService, DbTimeOffService>();

        builder.Services.AddHostedService<DatabaseAvailabilityMonitor>(sp =>
            new DatabaseAvailabilityMonitor(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<IHostEnvironment>(),
                sp.GetRequiredService<DatabaseRuntimeState>(),
                primaryPlugin!,
                primaryConnectionString,
                secondaryPlugin,
                secondaryConnectionString,
                sp.GetRequiredService<ILogger<DatabaseAvailabilityMonitor>>()));

        builder.Services.AddHostedService<StandbySynchronizationService>();

        var activePlugin = catalog.FindById(databaseState!.ActiveProviderId) ?? primaryPlugin!;
        var activeConnectionString = databaseState.ActiveProviderId == primaryPlugin!.Id
            ? primaryConnectionString
            : (secondaryConnectionString ?? primaryConnectionString);
        var standbyPlugin = databaseState.ActiveProviderId == primaryPlugin!.Id
            ? secondaryPlugin
            : primaryPlugin;
        var standbyConnectionString = databaseState.ActiveProviderId == primaryPlugin!.Id
            ? secondaryConnectionString
            : primaryConnectionString;

        builder.Services.AddPersistenceLayer(
            activePlugin,
            activeConnectionString,
            standbyPlugin,
            standbyConnectionString,
            databaseState);
        builder.Services.AddGatewayLayer();
        builder.Services.AddApplicationLayer();
        builder.Services.AddInfrastructureLayer();

        builder.Services.AddAppIdentity(databaseState);
    }
    else
    {
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
    }

    var app = builder.Build();

    if (databaseState is not null)
    {
        app.Logger.LogInformation(
            "Active database provider: {DatabaseProvider}. Primary ({PrimaryProvider}) available: {PrimaryAvailable}.",
            databaseState.ActiveProviderId,
            primaryPlugin!.DisplayName,
            databaseState.PrimaryAvailable);
    }

    app.UseAppLocalization();

    long migrationsMs = 0;
    long seedingMs = 0;
    long standbyBootstrapMs = 0;

    if (setupState.IsComplete)
    {
        (migrationsMs, seedingMs, standbyBootstrapMs) = await DatabaseStartupInitializer.InitializeAsync(
            app,
            databaseState!,
            catalog,
            primaryPlugin!,
            secondaryPlugin,
            secondaryConnectionString,
            builder.Configuration);
    }

    app.UseMiddleware<GlobalExceptionHandler>();
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found");
    app.UseMiddleware<SetupMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.MapStaticAssets();
    app.UseAppSerilogRequestLogging();
    app.MapControllers();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    if (setupState.IsComplete)
    {
        app.MapAuthEndpoints();
        app.MapEmployeeEndpoints();
    }

    var totalStartupMs = startupSw.ElapsedMilliseconds;

    var activePluginForBenchmark = catalog.FindById(databaseState?.ActiveProviderId ?? primaryPlugin?.Id ?? "") ?? primaryPlugin;
    if (activePluginForBenchmark != null && setupState.IsComplete)
    {
        await benchmarkStore.RecordStartupAsync(
            providerId: activePluginForBenchmark.Id,
            displayName: activePluginForBenchmark.DisplayName,
            totalStartupMs: totalStartupMs,
            failoverSelectMs: failoverSelectMs,
            migrationsMs: migrationsMs,
            seedingMs: seedingMs,
            standbyBootstrapMs: standbyBootstrapMs);
    }

    app.Logger.LogInformation(
        "[Startup Timings] Total: {TotalMs}ms | Active Provider: {Active} | Secondary: {Secondary} | FailoverCheck: {FailoverMs}ms | Migrations: {MigMs}ms | Seeding: {SeedMs}ms",
        totalStartupMs,
        databaseState?.ActiveProviderId ?? "None",
        databaseState?.SecondaryProviderId ?? "None",
        failoverSelectMs,
        migrationsMs,
        seedingMs);

    var timings = new StartupTimings(
        totalStartupMs,
        failoverSelectMs,
        migrationsMs,
        seedingMs,
        standbyBootstrapMs);

    app.MapHealthEndpoints(
        databaseState,
        timings,
        primaryPlugin,
        primaryConnectionString,
        secondaryPlugin,
        secondaryConnectionString,
        builder.Configuration);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
