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

            builder.Property(u => u.Name).IsRequired().HasMaxLength(200);
            builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
            builder.HasIndex(u => u.Email).IsUnique();

            // Self-referencing manager hierarchy: pair Manager (ManagerId) with DirectReports
            // so EF treats them as one relationship instead of two separate FKs.
            builder.HasOne(u => u.Manager)
                   .WithMany(u => u.DirectReports)
                   .HasForeignKey(u => u.ManagerId)
                   .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
