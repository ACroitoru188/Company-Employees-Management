namespace CompanyEmployees.Web.Models;

public class TeamMember
{
    public required string Name { get; set; }
    public required string Department { get; set; }
    public List<TimeOffRequest> Requests { get; set; } = [];

    public string Initials => string.Concat(
        Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part[0]));
}
