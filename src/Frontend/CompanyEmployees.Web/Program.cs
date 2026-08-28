using Blazored.LocalStorage;
using CompanyEmployees.Application;
using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Gateway;
using CompanyEmployees.Infrastructure;
using CompanyEmployees.Infrastructure.ExceptionHandling;
using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Web.Components;
using CompanyEmployees.Web.Plugins;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using CompanyEmployees.Web.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.FluentUI.AspNetCore.Components;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// provider discovery via ProviderLoader
// Bootstrap a minimal logger so ProviderLoader can report issues during startup
// (before the full DI container is built).
using var bootstrapFactory = LoggerFactory.Create(b =>
    b.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole());
var bootstrapLogger = bootstrapFactory.CreateLogger("Startup");

var plugins = ProviderLoader.Load(builder.Environment.ContentRootPath, bootstrapLogger);
var catalog = new DatabaseProviderCatalog(plugins, builder.Configuration);

// Load setup state from App_Data/setup-state.json
var setupStore = new JsonSetupStateStore(builder.Environment);
var setupState = setupStore.Load();

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

    databaseState = await DatabaseFailoverSelector.SelectAsync(
        primaryPlugin: primaryPlugin,
        primaryConnectionString: primaryConnectionString,
        secondaryPlugin: secondaryPlugin,
        secondaryConnectionString: secondaryConnectionString,
        configuration: builder.Configuration);
}

if (builder.Environment.IsDevelopment())
{
    // The Windows Event Log provider can require elevated access and must never make a local
    // authentication request fail merely because a warning could not be written there.
    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();

    // Keep development authentication independent of the current Windows profile. This also
    // lets the app run from restricted shells and containers that cannot write ASP.NET's
    // per-user default key directory.
    var relativeKeyPath = builder.Configuration["DataProtection:KeysPath"]
        ?? "../../../.tmp/data-protection-keys";
    var keyPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, relativeKeyPath));
    Directory.CreateDirectory(keyPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
        .SetApplicationName("CompanyEmployees");
}

builder.Services.AddRazorComponents()
    // Without this the circuit reports every unhandled exception to the browser as the same
    // "An unhandled error has occurred" bar, and the real message only exists in the server
    // console — which is no help at all when a page dies on a teammate's machine. Development
    // only: the detail includes stack traces.
    .AddInteractiveServerComponents(options =>
        options.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddFluentUIComponents();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<ThemeState>();

builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton<ISetupStateStore>(setupStore);
builder.Services.AddSingleton<AppLocalizer>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddSingleton<IAccountEmailSender, SmtpAccountEmailSender>();
builder.Services.AddControllers();
// Keep this even though the app maps no hub: these HubOptions also configure the Blazor
// circuit, and removing it drops the limit to the 32 KB default.
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
    builder.Services.AddPersistenceLayer(
        activePlugin,
        activeConnectionString,
        secondaryPlugin,
        secondaryConnectionString,
        databaseState);
    builder.Services.AddGatewayLayer();
    builder.Services.AddApplicationLayer();
    builder.Services.AddInfrastructureLayer();

    builder.Services.AddIdentity<User, IdentityRole<Guid>>()
        .AddEntityFrameworkStores<CompanyEmployeesDbContext>()
        .AddSignInManager()
        .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
        .AddDefaultTokenProviders();

    builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    {
        options.TokenLifespan = TimeSpan.FromHours(24);
    });

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<AuthenticationStateProvider, CompanyEmployees.Web.Security.IdentityRevalidatingAuthenticationStateProvider>();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = $"CompanyEmployees.Auth.{databaseState!.ActiveProviderId}";
        options.LoginPath = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(5);
        options.SlidingExpiration = true;
    });
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

// The language is carried by the standard culture cookie, written either at login (from the
// account's saved preference) or by the picker in the layout. No other provider is registered:
// the browser's Accept-Language must not override a choice the employee made explicitly.
var supportedCultures = SupportedLanguages.All
    .Select(language => new System.Globalization.CultureInfo(language.Culture))
    .ToArray();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(SupportedLanguages.DefaultCulture),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    RequestCultureProviders =
    [
        new CookieRequestCultureProvider()
    ]
});

if (setupState.IsComplete)
{
    if (databaseState!.IsFailoverActive)
    {
        using var scope = app.Services.CreateScope();
        await StandbyBootstrapper.EnsureReadyAsync(
            secondaryPlugin!,
            secondaryConnectionString!,
            builder.Configuration);
    }
    else if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
        var activePlugin = catalog.FindById(databaseState!.ActiveProviderId) ?? primaryPlugin!;
        await activePlugin.ApplyMigrationsAsync(db);
        await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, activePlugin);
        await DatabaseSeeder.SeedAsync(db);
    }

    // Production migrations can be applied out of process, but the cross-provider outbox is
    // deliberately provider-managed and must exist before the first business write.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
        var activePlugin = catalog.FindById(databaseState!.ActiveProviderId) ?? primaryPlugin!;
        await DatabaseOutboxSchemaInitializer.EnsureCreatedAsync(db, activePlugin);
    }
}

app.UseMiddleware<GlobalExceptionHandler>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
// app.UseHttpsRedirection(); // Comentat temporar pentru a permite testarea pe HTTP (port 5269)
app.UseMiddleware<SetupMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (setupState.IsComplete)
{
    app.MapPost("/api/auth/login", async (HttpContext context,
        [Microsoft.AspNetCore.Mvc.FromForm] string email,
        [Microsoft.AspNetCore.Mvc.FromForm] string password,
        [Microsoft.AspNetCore.Mvc.FromForm] Guid? regionId,
        [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl,
        [Microsoft.AspNetCore.Mvc.FromServices] SignInManager<User> signInManager,
        [Microsoft.AspNetCore.Mvc.FromServices] UserManager<User> userManager) =>
    {
        var account = await userManager.FindByNameAsync(email);
        var result = account == null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signInManager.CheckPasswordSignInAsync(account, password, lockoutOnFailure: false);

        var regionMatches = result.Succeeded
            && regionId.HasValue
            && account!.RegionId == regionId.Value
            && await userManager.Users.AnyAsync(user =>
                user.Id == account.Id && user.RegionId == regionId.Value && user.Region.IsActive);

        if(regionMatches)
        {
            // A login POST may be used to switch demo accounts. Explicitly remove the
            // previous principal before issuing the cookie for the selected account.
            await signInManager.SignOutAsync();
            await signInManager.SignInAsync(account!, isPersistent: true);

            // The saved preference follows the account, so signing in on another machine restores
            // the employee's language instead of whatever that browser last used.
            var culture = SupportedLanguages.Normalize(account!.PreferredCulture);
            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });

            var destination = await userManager.Users
                .Where(u => u.NormalizedUserName == email.ToUpperInvariant())
                .Select(u => new
                {
                    Department = u.Department != null ? u.Department.Name : null,
                    u.Role
                })
                .FirstOrDefaultAsync();

            if (returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
                return Results.Redirect(returnUrl);

            return Results.Redirect(HomeRouteResolver.Resolve(destination?.Role, destination?.Department));
        }
        else
        {
            return Results.Redirect("/?error=InvalidCredentials");
        }
    }).DisableAntiforgery();

    // Sign-out has to be a plain HTTP request too: an interactive circuit can't
    // touch the auth cookie (same reason the login form posts here).
    app.MapGet("/api/auth/logout", async (
        HttpContext context,
        ImpersonationContext impersonation,
        SignInManager<User> signInManager) =>
    {
        // Close any borrowed session first: a row left open here is indistinguishable from one
        // still in use, and used to block the next switch for good.
        var acting = ActingUser.Resolve(context.User);
        if (acting is not null)
            await impersonation.EndOpenSessionAsync(acting.RealUserId);

        await signInManager.SignOutAsync();
        return Results.Redirect("/");
    });

    // A provider change requires a new HTTP scope so every repository receives a DbContext built
    // for the newly selected provider. It also clears the old database's identity cookie.
    app.MapGet("/api/auth/database-switched", async (SignInManager<User> signInManager) =>
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/");
    }).RequireAuthorization();

    // Borrowing an account swaps the auth cookie, so it has to happen over a plain request
    // like login does. The rules live in ImpersonationContext; this only maps them to a
    // redirect. Both endpoints require an already-signed-in user.
    app.MapPost("/api/auth/impersonate", async (
        HttpContext context,
        [Microsoft.AspNetCore.Mvc.FromForm] Guid delegationId,
        ImpersonationContext impersonation,
        SignInManager<User> signInManager) =>
    {
        var acting = ActingUser.Resolve(context.User);
        if (acting is null)
            return Results.Redirect("/");

        // No chaining, decided from the cookie rather than from an open session row: the row
        // survives a sign-out or an expired cookie, the claim does not.
        if (acting.IsImpersonating)
            return Results.Redirect("/employee/dashboard?error=DelegationUnavailable");

        try
        {
            var target = await impersonation.StartAsync(
                acting.RealUserId, delegationId, context.Connection.RemoteIpAddress?.ToString());

            await signInManager.SignInWithClaimsAsync(target, isPersistent: true, new[]
            {
                new Claim(ImpersonationClaims.RealUserId, acting.RealUserId.ToString("D")),
                new Claim(ImpersonationClaims.RealUserName, acting.RealUserName),
                new Claim(ImpersonationClaims.DelegationId, delegationId.ToString("D"))
            });

            // Same landing rule as login: /manager/team bounces anyone who is not a
            // LineManager, and admins are delegated from too.
            return Results.Redirect(HomeRouteResolver.Resolve(target.Role, target.Department?.Name));
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Impersonation refused for user {RealUserId}.", acting.RealUserId);
            return Results.Redirect("/employee/dashboard?error=DelegationUnavailable");
        }
    }).DisableAntiforgery();

    // Unlike a delegation, an admin preview is inspection-only. It swaps the claims to the selected
    // account so page guards, navigation and read queries all show that account's perspective; the
    // explicit marker lets the layout make the application surface inert.
    app.MapPost("/api/auth/preview/start", async (
        HttpContext context,
        [Microsoft.AspNetCore.Mvc.FromForm] Guid userId,
        UserManager<User> userManager,
        SignInManager<User> signInManager) =>
    {
        var acting = ActingUser.Resolve(context.User);
        if (acting is null || acting.IsImpersonating)
            return Results.Redirect("/employee/dashboard?error=PreviewUnavailable");

        var realUser = await userManager.FindByIdAsync(acting.RealUserId.ToString());
        var target = await userManager.Users
            .Include(user => user.Department)
            .FirstOrDefaultAsync(user => user.Id == userId);
        if (realUser?.Role != UserRole.Admin || target is null || target.Id == realUser.Id || target.Status != UserStatus.Active)
            return Results.Redirect("/admin/users?error=PreviewUnavailable");

        await signInManager.SignInWithClaimsAsync(target, isPersistent: true, new[]
        {
            new Claim(ImpersonationClaims.RealUserId, realUser.Id.ToString("D")),
            new Claim(ImpersonationClaims.RealUserName, realUser.Name),
            new Claim(ImpersonationClaims.ReadOnlyPreview, "true"),
            new Claim(ImpersonationClaims.PreviewUserId, target.Id.ToString("D")),
            new Claim(ImpersonationClaims.PreviewUserName, target.Name)
        });

        return Results.Redirect(HomeRouteResolver.Resolve(target.Role, target.Department?.Name));
    }).RequireAuthorization().DisableAntiforgery();

    // GET so the banner's exit can be a plain link, like logout: a forced request only ever
    // returns someone to their own account, so there is nothing here worth a CSRF token.
    app.MapGet("/api/auth/impersonate/stop", async (
        HttpContext context,
        ImpersonationContext impersonation,
        SignInManager<User> signInManager) =>
    {
        var acting = ActingUser.Resolve(context.User);
        if (acting is null)
            return Results.Redirect("/");

        var realUser = await impersonation.StopAsync(acting.RealUserId);
        await signInManager.SignInAsync(realUser, isPersistent: true);

        return Results.Redirect("/employee/dashboard");
    });

    app.MapGet("/api/employees/export.csv", async (
        HttpContext httpContext,
        EmployeeCsvExportService csvExporter,
        UserManager<User> userManager,
        CancellationToken cancellationToken) =>
    {
        // Export scope always comes from the authenticated account in the database.
        // A UI preview selection must never expose another region's employee data.
        var email = httpContext.User.Identity?.Name;
        var regionId = await userManager.Users
            .Where(user => user.Email == email)
            .Select(user => (Guid?)user.RegionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!regionId.HasValue)
            return Results.NotFound();

        var export = await csvExporter.GenerateAsync(regionId.Value, cancellationToken);
        return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }).RequireAuthorization(policy => policy.RequireAssertion(context =>
        context.User.IsInRole(UserRole.Admin.ToString())
        || context.User.HasClaim("Department", HomeRouteResolver.HrDepartmentName)));
}


app.Run();

