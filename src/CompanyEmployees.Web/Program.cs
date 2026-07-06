using CompanyEmployees.Data;
using CompanyEmployees.Data.Entities;
using CompanyEmployees.Web.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();

// ponytail: dev-only demo login (demo@siemens.com / Passw0rd!) so Login.razor is testable
// before real employee signup exists. Delete once Identity migration lands.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (!db.Employees.Any(e => e.Email == "demo@siemens.com"))
    {
        var demo = new Employee
        {
            FirstName = "Demo",
            LastName = "User",
            Email = "demo@siemens.com",
            PhoneNumber = "000-000-0000",
            DateOfBirth = new DateTime(1990, 1, 1),
            Address = "N/A",
            HireDate = DateTime.UtcNow,
            Salary = 0,
            PasswordHash = "",
            CreatedAt = DateTime.UtcNow
        };
        demo.PasswordHash = new PasswordHasher<Employee>().HashPassword(demo, "Passw0rd!");
        db.Employees.Add(demo);
        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
