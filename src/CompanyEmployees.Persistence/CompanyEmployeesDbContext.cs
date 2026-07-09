using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence
{
    public class CompanyEmployeesDbContext : DbContext
    {
        public CompanyEmployeesDbContext(DbContextOptions<CompanyEmployeesDbContext> options)
        : base(options)
        {
        }

        public DbSet<Employee> Employees { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Role> Roles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyEmployeesDbContext).Assembly);
        }
    }
}
