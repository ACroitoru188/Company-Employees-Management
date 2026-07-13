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

        public DbSet<User> Users { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<LeaveApproval> LeaveApprovals { get; set; }
        public DbSet<LeaveAllocation> LeaveAllocations { get; set; }

        public DbSet<UserSession> UserSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyEmployeesDbContext).Assembly);
        }
    }
}
