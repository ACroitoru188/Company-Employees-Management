using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;

public class RoleAssignment
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public UserRole OldRole { get; set; }
    public UserRole NewRole { get; set; }

    public Guid? NewManagerId { get; set; }
    public User? NewManager { get; set; }

    public Guid AssignedById { get; set; } // must be an Admin
    public User AssignedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
