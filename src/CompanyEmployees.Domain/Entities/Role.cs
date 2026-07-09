using CompanyEmployees.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.Entities
{
    public class Role
    {
        public int RoleId { get; set; }
        public string Name { get; set; } = null!;
        public string? NormalizedName { get; set; }
        public string? ConcurrencyStamp { get; set; }

        public string Color { get; set; } = null!;
        public int Position { get; set; }
        public Permission Permissions { get; set; }

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }
}
