using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations;

public sealed class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        builder.HasKey(region => region.Id);
        builder.Property(region => region.Name).HasMaxLength(100).IsRequired();
        builder.Property(region => region.Code).HasMaxLength(8).IsRequired();
        builder.HasIndex(region => region.Code).IsUnique();
        builder.HasIndex(region => region.Name).IsUnique();
    }
}
