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
    public class ContractConfiguration : IEntityTypeConfiguration<Contract>
    {
        public void Configure(EntityTypeBuilder<Contract> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Type).IsRequired();
            builder.Property(c => c.Status).IsRequired();
            builder.Property(c => c.StartDate).IsRequired();
            // Relatia cu User (Foreign Key UserId):
            // Un contract apartine unui User, iar un User are mai multe Contracte
            builder.HasOne(c => c.User)
                   .WithMany(u => u.Contracts)
                   .HasForeignKey(c => c.UserId)
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
