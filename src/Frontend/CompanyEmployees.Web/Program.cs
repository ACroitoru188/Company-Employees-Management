using Azure.Identity;
using Blazored.LocalStorage;
using CompanyEmployees.Application;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Gateway;
using CompanyEmployees.Infrastructure;
using CompanyEmployees.Infrastructure.ExceptionHandling;
using CompanyEmployees.Persistence;
using CompanyEmployees.Web.Components;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Localization;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddMudLocalization();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<ThemeState>();
builder.Services.AddScoped<EmployeeAccountService>();
builder.Services.AddScoped<EmployeeCsvExportService>();
builder.Services.AddScoped<LanguagePreferenceService>();
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

// Employee UI reads real data through EmployeeContext; InMemoryTimeOffService
// remains available as a mock if the DB is unreachable.
builder.Services.AddScoped<ITimeOffService, DbTimeOffService>();

builder.Services.AddPersistenceLayer(builder.Configuration);
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
    options.LoginPath = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(5);
    options.SlidingExpiration = true;
});

var app = builder.Build();

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

if (app.Environment.IsDevelopment())
{
    // Applies any pending migrations (creating the database if it does not exist yet).
    // The demo accounts and their leave data arrive through the SeedDemoData migration,
    // so a fresh clone only needs to run the app. Nothing is dropped: data entered
    // through the UI survives restarts.
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
    db.Database.Migrate();
    SeedContracts(db);
}

app.UseMiddleware<GlobalExceptionHandler>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
// app.UseHttpsRedirection(); // Comentat temporar pentru a permite testarea pe HTTP (port 5269)
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/api/auth/login", async (HttpContext context,
    [Microsoft.AspNetCore.Mvc.FromForm] string email,
    [Microsoft.AspNetCore.Mvc.FromForm] string password,
    [Microsoft.AspNetCore.Mvc.FromForm] Guid? regionId,
    [Microsoft.AspNetCore.Mvc.FromForm] string? returnUrl,
    SignInManager<User> signInManager,
    UserManager<User> userManager) =>
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
app.MapGet("/api/auth/logout", async (SignInManager<User> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
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


app.Run();

static void SeedContracts(CompanyEmployeesDbContext db)
{
    var users = db.Users.Include(u => u.Contracts).ToList();
    if (users.Count == 0) return;

    var random = new Random(12345);
    bool modified = false;

    foreach (var user in users)
    {
        if (user.Contracts == null || user.Contracts.Count == 0)
        {
            // ~40% Determinate, ~60% Indeterminate
            var isDeterminate = random.Next(100) < 40;

            // Start date between 1 and 4 years ago (e.g. 2022 to 2025)
            var startDaysAgo = random.Next(200, 1400);
            var startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-startDaysAgo));
            DateOnly? endDate = null;

            if (isDeterminate)
            {
                // End date between 3 months and 18 months in the future
                var endDaysFuture = random.Next(90, 550);
                endDate = DateOnly.FromDateTime(DateTime.Today.AddDays(endDaysFuture));
            }

            var contract = new Contract
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Type = isDeterminate ? ContractType.Determinate : ContractType.Indeterminate,
                Status = ContractStatus.Active,
                StartDate = startDate,
                EndDate = endDate,
                Notes = isDeterminate ? "Individual fixed-term employment contract" : "Individual permanent employment contract",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Contracts.Add(contract);
            modified = true;
        }
    }

    // Sanitize any existing contract notes from Romanian to English
    var allContracts = db.Contracts.ToList();
    foreach (var contract in allContracts)
    {
        if (contract.Notes == "Contract individual pe perioadă determinată")
        {
            contract.Notes = "Individual fixed-term employment contract";
            modified = true;
        }
        else if (contract.Notes == "Contract individual pe perioadă nedeterminată")
        {
            contract.Notes = "Individual permanent employment contract";
            modified = true;
        }
    }

    if (modified)
    {
        db.SaveChanges();
    }
}

