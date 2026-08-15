using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Domain.Entities;

// Written once, never updated: the decision tables record the borrowed account as the
// author, so this is the only place that says which human was behind it. It survives
// reorganisations for the same reason — nothing here is recomputed from the current
// reporting graph.
public class DelegatedAction
{
    public Guid Id { get; set; }

    public Guid DelegationId { get; set; }
    public ManagerDelegation Delegation { get; set; } = null!;

    // The human at the keyboard.
    public Guid RealUserId { get; set; }
    public User RealUser { get; set; } = null!;

    // The account whose authority was used, and which the decision tables credit.
    public Guid ActedAsUserId { get; set; }
    public User ActedAsUser { get; set; } = null!;

    // The employee the action landed on.
    public Guid TargetUserId { get; set; }
    public User TargetUser { get; set; } = null!;

    public DelegatedActionType ActionType { get; set; }

    // The leave request or contract. Not a foreign key: one column cannot point at two
    // tables, and the row has to outlive whatever it refers to.
    public Guid TargetEntityId { get; set; }

    // Human-readable specifics — the leave period, the new end date, the reason given.
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }
}
