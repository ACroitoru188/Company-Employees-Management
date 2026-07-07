using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Data.Entities;

// Id și Name vin din IdentityRole<int>.
public class Role : IdentityRole<int>
{
    public string Color { get; set; } = null!;
    public int Position { get; set; }
    public Permission Permissions { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
