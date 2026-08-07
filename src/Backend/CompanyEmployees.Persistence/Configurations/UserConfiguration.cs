using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasKey(u => u.Id);

            // Self-referencing manager hierarchy: pair Manager (ManagerId) with DirectReports
            // so EF treats them as one relationship instead of two separate FKs.
            builder.HasOne(u => u.Manager)
                   .WithMany(u => u.DirectReports)
                   .HasForeignKey(u => u.ManagerId)
                   .OnDelete(DeleteBehavior.NoAction);

            // apartenenta la departament: stergerea unui departament pune null pe departmentid al membrilor.
            builder.HasOne(u => u.Department)
                   .WithMany(d => d.Members)
                   .HasForeignKey(u => u.DepartmentId)
                   .OnDelete(DeleteBehavior.SetNull);

            // A region cannot be deleted while it is the security scope of an account.
            builder.HasOne(u => u.Region)
                   .WithMany(r => r.Users)
                   .HasForeignKey(u => u.RegionId)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
