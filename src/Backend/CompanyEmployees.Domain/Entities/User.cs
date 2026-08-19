using CompanyEmployees.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Domain.Entities;

public class User : IdentityUser<Guid>
{
    //inf. comune
    public string Name { get; set; } = string.Empty;

    //rol si status
    public UserRole Role { get; set; } = UserRole.Guest;
    public UserStatus Status { get; set; } = UserStatus.Active;
    
    //daca e employee, are un manager care il gestioneaza
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }
    public ICollection<User> DirectReports { get; set; } = new List<User>(); //< cine ii raporteaza managerului
    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    //departamentul din care face parte (echipa)
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string City { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;

    // Current employment region. This is the authorization boundary used when
    // showing employees, leave requests, dashboards and exports.
    public Guid RegionId { get; set; }
    public Region Region { get; set; } = null!;

    // UI language chosen by the employee. Null means the application default (English).
    // This preference is deliberately independent from the employee's security region.
    public string? PreferredCulture { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
