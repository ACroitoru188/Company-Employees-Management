using CompanyEmployees.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Data;

public class ApplicationDbContext : IdentityDbContext<Employee, Role, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(entity =>
        {
            // Păstrăm numele de domeniu în loc de AspNetUsers/AspNetRoles/AspNetUserRoles.
            entity.ToTable("Employees");

            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PhoneNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Address).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Salary).HasPrecision(18, 2);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            // La ștergerea unui Department, angajații rămân (DepartmentId devine null).
            entity.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            // Navigație many-to-many peste tabelul de join al Identity (cascade implicit,
            // ca vechiul EmployeeRole).
            entity.HasMany(e => e.Roles)
                .WithMany(r => r.Employees)
                .UsingEntity<IdentityUserRole<int>>(
                    join => join.HasOne<Role>().WithMany().HasForeignKey(ur => ur.RoleId),
                    join => join.HasOne<Employee>().WithMany().HasForeignKey(ur => ur.UserId));
        });

        modelBuilder.Entity<IdentityUserRole<int>>(entity => entity.ToTable("EmployeeRoles"));

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
            entity.ToTable("Roles");

            entity.Property(r => r.Color).HasMaxLength(7).IsRequired();

            // ConcurrencyStamp fix, altfel HasData generează o migrare nouă la fiecare build.
            entity.HasData(
                new Role
                {
                    Id = 1,
                    Name = "Admin",
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = "a3b9c1d4-0000-4000-8000-000000000001",
                    Color = "#ED4245",
                    Position = 3,
                    Permissions = Permission.Administrator
                },
                new Role
                {
                    Id = 2,
                    Name = "Department Manager",
                    NormalizedName = "DEPARTMENT MANAGER",
                    ConcurrencyStamp = "a3b9c1d4-0000-4000-8000-000000000002",
                    Color = "#5865F2",
                    Position = 2,
                    Permissions = Permission.ViewEmployees | Permission.EditEmployees |
                                  Permission.ViewSalaries | Permission.ManageDepartments
                },
                new Role
                {
                    Id = 3,
                    Name = "Employee",
                    NormalizedName = "EMPLOYEE",
                    ConcurrencyStamp = "a3b9c1d4-0000-4000-8000-000000000003",
                    Color = "#99AAB5",
                    Position = 1,
                    Permissions = Permission.ViewEmployees
                });
        });
    }
}
