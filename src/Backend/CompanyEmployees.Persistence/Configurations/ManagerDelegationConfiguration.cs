using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Persistence.Configurations
{
    public class ManagerDelegationConfiguration : IEntityTypeConfiguration<ManagerDelegation>
    {
        public void Configure(EntityTypeBuilder<ManagerDelegation> builder)
        {
            builder.HasKey(md => md.Id);
            builder.Property(md => md.StartDate).IsRequired();
            builder.Property(md => md.EndDate).IsRequired();
            // 1. Relatia cu Managerul care deleaga:
            builder.HasOne(md => md.Manager)
                   .WithMany()
                   .HasForeignKey(md => md.ManagerId)
                   .OnDelete(DeleteBehavior.NoAction);
            // 2. Relatia cu Persoana Delegata:
            builder.HasOne(md => md.Delegate)
                   .WithMany()
                   .HasForeignKey(md => md.DelegateId)
                   .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
