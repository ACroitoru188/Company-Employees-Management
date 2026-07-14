using CompanyEmployees.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Domain.Entities;

public class User : IdentityUser<Guid>
{
    //inf. comune
    public string Name { get; set; }

    //rol si status
    public UserRole Role { get; set; } = UserRole.Guest;
    public UserStatus Status { get; set; } = UserStatus.Active;
    
    //daca e employee, are un manager care il gestioneaza
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }
    public ICollection<User> DirectReports { get; set; } = new List<User>(); //< cine ii raporteaza managerului
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}