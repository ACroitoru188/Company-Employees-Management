namespace CompanyEmployees.Persistence;

/// <summary>
/// Implemented by classes that can replicate data between a primary and a standby database.
/// The host resolves this service from DI and calls it during provider switches and drain operations.
/// </summary>
public interface IStandbyReplicationService
{
    /// <summary>
    /// Returns true when this service can handle replication from
    /// <paramref name="primaryId"/> to <paramref name="secondaryId"/>.
    /// </summary>
    bool CanReplicate(string primaryId, string secondaryId);

    /// <summary>Rebuilds the standby from a fresh snapshot of the primary.</summary>
    Task SynchronizeAsync(CancellationToken ct = default);
}
