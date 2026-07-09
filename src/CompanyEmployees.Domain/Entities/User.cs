using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;

public class User
{
    //inf. comune
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }

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