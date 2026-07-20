using Azure.Identity;
using CompanyEmployees.Application;
using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Gateway;
using CompanyEmployees.Infrastructure;
using CompanyEmployees.Infrastructure.ExceptionHandling;
using CompanyEmployees.Persistence;
using CompanyEmployees.Web.Components;
using CompanyEmployees.Application.Hubs;
using CompanyEmployees.Web.Security;
using CompanyEmployees.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddControllers();
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
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, CompanyEmployees.Web.Security.IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(5);
    options.SlidingExpiration = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Applies any pending migrations (creating the database if it does not exist yet).
    // The demo accounts and their leave data arrive through the SeedDemoData migration,
    // so a fresh clone only needs to run the app. Nothing is dropped: data entered
    // through the UI survives restarts.
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
    db.Database.Migrate();
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
    SignInManager<User> signInManager,
    UserManager<User> userManager) =>
{
    var result = await signInManager.PasswordSignInAsync(
        userName: email,
        password: password,
        isPersistent: true,
        lockoutOnFailure: false);

    if(result.Succeeded)
    {
        // Managers start on their team dashboard; other HR staff on the HR one;
        // everyone else on the employee dashboard. HR is a department while manager
        // is a UserRole, which is why both are needed. Role wins, so an HR line
        // manager lands on her team and reaches HR via the drawer link.
        var account = await userManager.Users
            .Where(u => u.NormalizedUserName == email.ToUpperInvariant())
            .Select(u => new
            {
                Department = u.Department != null ? u.Department.Name : null,
                u.Role
            })
            .FirstOrDefaultAsync();

        if (account != null &&
            (account.Role == UserRole.LineManager || account.Role == UserRole.ProjectManager))
            return Results.Redirect("/manager/team");

        if (account?.Department == "HR")
            return Results.Redirect("/hr/dashboard");

        return Results.Redirect("/employee/dashboard");
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

app.MapHub<NotificationHub>("/notificationHub");



app.Run();
