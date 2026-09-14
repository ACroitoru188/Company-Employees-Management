namespace CompanyEmployees.Persistence.Configurations;

using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class LeaveRequestDocumentConfiguration : IEntityTypeConfiguration<LeaveRequestDocument>
{
    public void Configure(EntityTypeBuilder<LeaveRequestDocument> builder)
    {
        builder.HasKey(d => d.Id);
        
        builder.Property(d => d.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.HashedFileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();

        builder.HasOne(d => d.LeaveRequest)
            .WithMany(r => r.Documents)
            .HasForeignKey(d => d.LeaveRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
