using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations
{
    public class CompanyEventConfiguration : IEntityTypeConfiguration<CompanyEvent>
    {
        public void Configure(EntityTypeBuilder<CompanyEvent> builder)
        {
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Title).IsRequired().HasMaxLength(150);
            builder.Property(e => e.Description).HasMaxLength(500);
            builder.Property(e => e.Category).IsRequired().HasMaxLength(50);
            builder.Property(e => e.RegionCode).HasMaxLength(10);
            builder.Property(e => e.Date).IsRequired();

            builder.HasIndex(e => new { e.Date, e.RegionCode });
        }
    }
}
