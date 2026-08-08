namespace CompanyEmployees.Domain.Entities;

// One stretch of time during which RealUser was signed in as ActedAsUser. The row is the
// only durable record that an account was borrowed — the auth cookie itself leaves nothing
// behind once it expires.
public class ImpersonationSession
{
    public Guid Id { get; set; }

    // The delegation that authorised this. Without it there is no way to switch.
    public Guid DelegationId { get; set; }
    public ManagerDelegation Delegation { get; set; } = null!;

    // The human at the keyboard.
    public Guid RealUserId { get; set; }
    public User RealUser { get; set; } = null!;

    // The account they were using.
    public Guid ActedAsUserId { get; set; }
    public User ActedAsUser { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? IpAddress { get; set; }
}
