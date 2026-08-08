namespace CompanyEmployees.Application
{
    // Supplied by the Web layer when the caller is inside someone else's account; null means
    // they are acting as themselves. Passed explicitly rather than read from an ambient
    // service so a method's authority is visible in its signature.
    public sealed record ActingOnBehalf(Guid RealUserId, Guid DelegationId);
}
