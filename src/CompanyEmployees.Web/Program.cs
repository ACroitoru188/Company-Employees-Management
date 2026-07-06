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

// ponytail: dev-only demo logins (Passw0rd!), one per role, so Login.razor and the
// permission model are testable before real employee signup exists. Delete once
// Identity migration lands.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    void SeedDemoUser(string email, string firstName, string lastName, int roleId)
    {
        if (db.Employees.Any(e => e.Email == email))
            return;

        var user = new Employee
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = "000-000-0000",
            DateOfBirth = new DateTime(1990, 1, 1),
            Address = "N/A",
            HireDate = DateTime.UtcNow,
            Salary = 0,
            PasswordHash = "",
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = new PasswordHasher<Employee>().HashPassword(user, "Passw0rd!");
        user.Roles.Add(db.Roles.Find(roleId)!);
        db.Employees.Add(user);
        db.SaveChanges();
    }

    SeedDemoUser("employee@siemens.com", "Demo", "Employee", roleId: 3);
    SeedDemoUser("linemanager@siemens.com", "Demo", "Manager", roleId: 2);
    SeedDemoUser("itadmin@siemens.com", "Demo", "Admin", roleId: 1);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
