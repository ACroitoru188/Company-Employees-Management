using CompanyEmployees.Domain.Constants;
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
    public class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.HasKey(r => r.RoleId);

            builder.Property(r => r.Name).IsRequired().HasMaxLength(50);
            builder.Property(r => r.Color).IsRequired();

            builder.HasIndex(r => r.Name).IsUnique();

            builder.HasData(
                new Role
                {
                    RoleId = RoleConstants.EmployeeRoleId,
                    Name = "Employee",
                    Color = "#808080",
                    Position = 1
                },
                new Role
                {
                    RoleId = RoleConstants.ManagerRoleId,
                    Name = "Manager",
                    Color = "#0000FF",
                    Position = 2 },
                new Role
                {
                    RoleId = RoleConstants.AdministratorRoleId,
                    Name = "Administrator",
                    Color = "#FF0000",
                    Position = 3
                },
                new Role
                { 
                    RoleId = RoleConstants.SuperAdminRoleId,
                    Name = "SuperAdmin",
                    Color = "#00FF00",
                    Position = 4
                }
            );
        }
    }
}
