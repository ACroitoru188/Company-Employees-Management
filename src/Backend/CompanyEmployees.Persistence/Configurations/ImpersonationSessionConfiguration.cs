using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations
{
    public class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
    {
        public void Configure(EntityTypeBuilder<ImpersonationSession> builder)
        {
            builder.HasKey(s => s.Id);

            builder.Property(s => s.IpAddress).HasMaxLength(45); // fits IPv6

            // NoAction on every user FK: three paths into AspNetUsers from one table would
            // otherwise give SQL Server a cascade cycle and the migration would be rejected.
            builder.HasOne(s => s.RealUser)
                   .WithMany()
                   .HasForeignKey(s => s.RealUserId)
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(s => s.ActedAsUser)
                   .WithMany()
                   .HasForeignKey(s => s.ActedAsUserId)
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(s => s.Delegation)
                   .WithMany()
                   .HasForeignKey(s => s.DelegationId)
                   .OnDelete(DeleteBehavior.NoAction);

            // The open-session lookup runs on every switch and every exit.
            builder.HasIndex(s => new { s.RealUserId, s.EndedAt });
        }
    }
}
