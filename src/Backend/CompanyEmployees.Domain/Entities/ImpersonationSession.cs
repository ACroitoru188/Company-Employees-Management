namespace CompanyEmployees.Domain.Entities;

public class ImpersonationSession
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }
    public User Admin { get; set; }

    public Guid TargetUserId { get; set; }
    public User TargetUser { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? IpAddress { get; set; }
}