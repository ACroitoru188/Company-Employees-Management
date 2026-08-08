using CompanyEmployees.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence
{
    public class CompanyEmployeesDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
    {
        public CompanyEmployeesDbContext(DbContextOptions<CompanyEmployeesDbContext> options)
        : base(options)
        {
        }

        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<LeaveApproval> LeaveApprovals { get; set; }
        public DbSet<LeaveAllocation> LeaveAllocations { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<ManagerDelegation> ManagerDelegations { get; set; }
        public DbSet<ImpersonationSession> ImpersonationSessions { get; set; }
        public DbSet<DelegatedAction> DelegatedActions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyEmployeesDbContext).Assembly);
        }
    }
}
