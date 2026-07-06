namespace CompanyEmployees.Data.Entities;

public class EmployeeRole
{
    public int EmployeeId { get; set; }
    public int RoleId { get; set; }

    public Employee Employee { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
