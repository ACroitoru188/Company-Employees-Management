namespace CompanyEmployees.Domain.Entities;

public class Region
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
}
