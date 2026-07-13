namespace CompanyEmployees.Persistence.Configurations;

using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



public class LeaveAllocationConfiguration : IEntityTypeConfiguration<LeaveAllocation>
{
    public void Configure(EntityTypeBuilder<LeaveAllocation> builder)
    {
        builder.HasKey(la => la.Id);

        builder.Property(la => la.NumberOfDays)
            .IsRequired();

        builder.Property(la => la.LeaveType)
            .IsRequired();

        builder.Property(la => la.CreatedAt)
            .IsRequired();

        builder.Property(la => la.UserId)
            .IsRequired();

        builder.HasOne(la => la.User)
            .WithMany()
            .HasForeignKey(la => la.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasIndex(la => new { la.UserId,            
                la.LeaveType, la.Year })                           
            .IsUnique();
    }
}
