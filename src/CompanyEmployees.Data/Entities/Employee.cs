using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Data.Entities;

// Id, Email, PhoneNumber și PasswordHash vin din IdentityUser<int>.
public class Employee : IdentityUser<int>
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public DateTime DateOfBirth { get; set; }
    public string Address { get; set; } = null!;
    public DateTime HireDate { get; set; }
    public decimal Salary { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Department? Department { get; set; }
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Department> ManagedDepartments { get; set; } = new List<Department>();
}
