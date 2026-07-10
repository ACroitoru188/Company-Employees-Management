//using CompanyEmployees.Web.Components;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.AspNetCore.Authentication.JwtBearer;
//using Microsoft.IdentityModel.Tokens;
//using System.Text;
//using CompanyEmployees.Application;
//using CompanyEmployees.Infrastructure;
//using CompanyEmployees.Persistence;
//using CompanyEmployees.Domain.Entities;
//using CompanyEmployees.Domain.Enums;
//using CompanyEmployees.Domain.GatewayInterfaces;
//using CompanyEmployees.Domain.Interfaces;
//using CompanyEmployees.Gateway.Repositories;

//var builder = WebApplication.CreateBuilder(args);

//// Add services to the container.
//builder.Services.AddRazorComponents()
//    .AddInteractiveServerComponents();

//builder.Services.AddDbContext<CompanyEmployeesDbContext>(options =>
//    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

//builder.Services.AddControllers();
//builder.Services.AddScoped<IUserGateway, UserRepository>();
//builder.Services.AddApplicationLayer();
//builder.Services.AddInfrastructureLayer();

//var secretKey = builder.Configuration.GetSection("JwtSettings:Secret").Value;
//var key = Encoding.UTF8.GetBytes(secretKey ?? throw new InvalidOperationException("Missing JwtSettings:Secret"));

//builder.Services.AddAuthentication(options =>
//{
//    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
//    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
//}).AddJwtBearer(options =>
//{
//    options.TokenValidationParameters = new TokenValidationParameters
//    {
//        ValidateIssuerSigningKey = true,
//        IssuerSigningKey = new SymmetricSecurityKey(key),
//        ValidateIssuer = false,
//        ValidateAudience = false
//    };
//});

//var app = builder.Build();

//// ponytail: dev-only demo logins (Passw0rd!), one per role, so Login.razor and the
//// permission model are testable before real user signup exists.
//if (app.Environment.IsDevelopment())
//{
//    using var scope = app.Services.CreateScope();
//    var db = scope.ServiceProvider.GetRequiredService<CompanyEmployeesDbContext>();
//    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

//    void SeedDemoUser(string email, string name, UserRole role)
//    {
//        if (db.Users.Any(u => u.Email == email))
//            return;

//        db.Users.Add(new User
//        {
//            Name = name,
//            Email = email,
//            PasswordHash = hasher.HashPassword("Passw0rd!"),
//            Role = role,
//            Status = UserStatus.Active,
//            CreatedAt = DateTime.UtcNow,
//            UpdatedAt = DateTime.UtcNow
//        });
//        db.SaveChanges();
//    }

//    SeedDemoUser("employee@siemens.com", "Demo Employee", UserRole.Employee);
//    SeedDemoUser("linemanager@siemens.com", "Demo Manager", UserRole.ProjectManager);
//    SeedDemoUser("itadmin@siemens.com", "Demo Admin", UserRole.Admin);
//}

//// Configure the HTTP request pipeline.
//if (!app.Environment.IsDevelopment())
//{
//    app.UseExceptionHandler("/Error");
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//    app.UseHsts();
//}
//app.UseStatusCodePagesWithReExecute("/not-found");
//app.UseHttpsRedirection();

//app.UseAuthentication();
//app.UseAuthorization();
//app.UseAntiforgery();

//app.MapStaticAssets();
//app.MapControllers();
//app.MapRazorComponents<App>()
//    .AddInteractiveServerRenderMode();

//app.Run();

using CompanyEmployees.Web.Components;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MudBlazor.Services;

using CompanyEmployees.Application;
using CompanyEmployees.Infrastructure;
using CompanyEmployees.Persistence;
using CompanyEmployees.Gateway;
using CompanyEmployees.Infrastructure.ExceptionHandling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddControllers();

builder.Services.AddPersistenceLayer(builder.Configuration);
builder.Services.AddGatewayLayer();
builder.Services.AddApplicationLayer();
builder.Services.AddInfrastructureLayer();

var secretKey = builder.Configuration.GetSection("JwtSettings:Secret").Value;
var key = Encoding.UTF8.GetBytes(secretKey ??
                                throw new InvalidOperationException("Missing JwtSettings:Secret"));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    seeder.Seed();
}

app.UseMiddleware<GlobalExceptionHandler>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.Run();
