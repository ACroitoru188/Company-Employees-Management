using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.Entities
{
    public class ManagerDelegation
    {
        public Guid Id { get; set; }
        public Guid ManagerId { get; set; }
        public User Manager { get; set; } = null!;
        public Guid DelegateId { get; set; }
        public User Delegate { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string? Reason { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }
}
