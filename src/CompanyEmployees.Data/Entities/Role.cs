namespace CompanyEmployees.Data.Entities;

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;
    public int Position { get; set; }
    public Permission Permissions { get; set; }

    public ICollection<EmployeeRole> EmployeeRoles { get; set; } = new List<EmployeeRole>();
}
