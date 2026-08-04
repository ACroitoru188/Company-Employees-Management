namespace CompanyEmployees.Persistence.Configurations;

using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



public class LeaveApprovalConfiguration : IEntityTypeConfiguration<LeaveApproval>
{
    public void Configure(EntityTypeBuilder<LeaveApproval> builder)
    {
        builder.HasKey(la => la.Id);

        builder.Property(la => la.Status).IsRequired();
        builder.Property(la => la.ApproverId).IsRequired();

        // Deleting a request deletes its approvals; deleting a user who approved
        // requests must NOT cascade (SQL Server rejects two cascade paths from Users).
        builder.HasOne(la => la.LeaveRequest)
            .WithMany(lr => lr.Approvals)
            .HasForeignKey(la => la.LeaveRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        
        builder.HasOne(la => la.Approver)
            .WithMany()
            .HasForeignKey(la => la.ApproverId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}