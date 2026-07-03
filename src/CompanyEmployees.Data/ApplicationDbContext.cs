using CompanyEmployees.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PhoneNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Address).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Salary).HasPrecision(18, 2);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            // La ștergerea unui Department, angajații rămân (DepartmentId devine null).
            entity.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(d => d.Name).IsUnique();
            entity.Property(d => d.Description).HasMaxLength(1000);

            // NoAction (nu SetNull): SQL Server respinge cicluri de acțiuni referențiale
            // între Employee.DepartmentId (SET NULL) și Department.ManagerId.
            // Un manager trebuie deci detașat explicit înainte de ștergere.
            entity.HasOne(d => d.Manager)
                .WithMany(e => e.ManagedDepartments)
                .HasForeignKey(d => d.ManagerId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(r => r.Name).IsUnique();
            entity.Property(r => r.Color).HasMaxLength(7).IsRequired();

            entity.HasData(
                new Role
                {
                    Id = 1,
                    Name = "Admin",
                    Color = "#ED4245",
                    Position = 3,
                    Permissions = Permission.Administrator
                },
                new Role
                {
                    Id = 2,
                    Name = "Department Manager",
                    Color = "#5865F2",
                    Position = 2,
                    Permissions = Permission.ViewEmployees | Permission.EditEmployees |
                                  Permission.ViewSalaries | Permission.ManageDepartments
                },
                new Role
                {
                    Id = 3,
                    Name = "Employee",
                    Color = "#99AAB5",
                    Position = 1,
                    Permissions = Permission.ViewEmployees
                });
        });

        modelBuilder.Entity<EmployeeRole>(entity =>
        {
            entity.HasKey(er => new { er.EmployeeId, er.RoleId });

            entity.HasOne(er => er.Employee)
                .WithMany(e => e.EmployeeRoles)
                .HasForeignKey(er => er.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(er => er.Role)
                .WithMany(r => r.EmployeeRoles)
                .HasForeignKey(er => er.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
