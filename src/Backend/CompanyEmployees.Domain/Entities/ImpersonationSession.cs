namespace CompanyEmployees.Domain.Entities;

public class ImpersonationSession
{
    public Guid Id { get; set; }

    public Guid AdminId { get; set; }
    public User Admin { get; set; } = null!;

    public Guid TargetUserId { get; set; }
    public User TargetUser { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? IpAddress { get; set; }
}
