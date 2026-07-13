namespace CompanyEmployees.Persistence.Configurations;

using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


public class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
            builder.HasKey(r => r.Id);                 
            
            builder.Property(r =>                      
                r.Reason).HasMaxLength(500);   
            
            builder.HasOne(r => r.User)                
                .WithMany()                         
                .HasForeignKey(r => r.UserId);      
            
            builder.HasIndex(r => r.UserId);           
    }
}