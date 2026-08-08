using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations
{
    public class DelegatedActionConfiguration : IEntityTypeConfiguration<DelegatedAction>
    {
        public void Configure(EntityTypeBuilder<DelegatedAction> builder)
        {
            builder.HasKey(a => a.Id);

            builder.Property(a => a.Details).HasMaxLength(500);

            // NoAction on all three user FKs: SQL Server rejects the migration otherwise,
            // and an audit row must not disappear with the people it names.
            builder.HasOne(a => a.RealUser)
                   .WithMany()
                   .HasForeignKey(a => a.RealUserId)
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(a => a.ActedAsUser)
                   .WithMany()
                   .HasForeignKey(a => a.ActedAsUserId)
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(a => a.TargetUser)
                   .WithMany()
                   .HasForeignKey(a => a.TargetUserId)
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(a => a.Delegation)
                   .WithMany()
                   .HasForeignKey(a => a.DelegationId)
                   .OnDelete(DeleteBehavior.NoAction);

            // The two personal views filter on one of these and sort by date.
            builder.HasIndex(a => new { a.ActedAsUserId, a.CreatedAt });
            builder.HasIndex(a => new { a.RealUserId, a.CreatedAt });

            // The admin view filters by region through a join, so nothing above helps its
            // ordering — this covers the sort for the whole-table pass.
            builder.HasIndex(a => a.CreatedAt);
        }
    }
}
