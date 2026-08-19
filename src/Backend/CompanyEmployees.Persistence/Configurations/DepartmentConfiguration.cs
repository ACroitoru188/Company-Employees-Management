using CompanyEmployees.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompanyEmployees.Persistence.Configurations
{
    public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
    {
        public void Configure(EntityTypeBuilder<Department> builder)
        {
            builder.HasKey(d => d.Id);
            builder.Property(d => d.Name).IsRequired().HasMaxLength(100);

            // withmany() fara inversa — tine asta separat de user.directreports.
            // noaction (nu setnull) evita ciclul de cascade fk cu user.departmentid;
            // detaseaza un manager inainte sa il stergi.
            builder.HasOne(d => d.Manager)
                   .WithMany()
                   .HasForeignKey(d => d.ManagerId)
                   .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
