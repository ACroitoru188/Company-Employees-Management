namespace CompanyEmployees.Domain.Entities;

using CompanyEmployees.Domain.Enums;

public class LeaveAllocation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; }
    public LeaveType LeaveType { get; set; }
    public int Year { get; set; }
    public int NumberOfDays { get; set; }
    public DateTime CreatedAt { get; set; }
}