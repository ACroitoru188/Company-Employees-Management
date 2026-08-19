namespace CompanyEmployees.Domain.Entities;

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // managerul de deasupra acestui departament (un linemanager). separat de user.managerid.
    public Guid? ManagerId { get; set; }
    public User? Manager { get; set; }

    // admin-ul de sediu (Site Admin) responsabil pentru acest departament.
    public Guid? AdminId { get; set; }
    public User? Admin { get; set; }

    public ICollection<User> Members { get; set; } = new List<User>();
}
