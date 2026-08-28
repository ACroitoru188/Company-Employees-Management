namespace CompanyEmployees.Persistence;

/// <summary>
/// Process-wide database status. It lives outside either database so the admin can still see
/// and change provider while the primary is unavailable.
/// </summary>
public sealed class DatabaseRuntimeState
{
    private readonly object _sync = new();
    private string _activeProviderId;
    private string _activeEfProviderName;
    private bool _primaryAvailable;
    private bool _secondaryAvailable;
    private string? _failoverReason;
    private int _pendingReplicationChanges;
    private DateTime? _oldestPendingChangeUtc;
    private DateTime? _lastSynchronizedUtc;
    private string? _replicationError;

    public DatabaseRuntimeState(
        string primaryProviderId,
        string activeProviderId,
        string activeEfProviderName,
        bool primaryAvailable,
        string supportContact,
        string? secondaryProviderId = null,
        string? failoverReason = null,
        bool secondaryAvailable = false)
    {
        PrimaryProviderId = primaryProviderId;
        SecondaryProviderId = secondaryProviderId;
        _activeProviderId = activeProviderId;
        _activeEfProviderName = activeEfProviderName;
        _primaryAvailable = primaryAvailable;
        _secondaryAvailable = secondaryAvailable;
        _failoverReason = failoverReason;
        SupportContact = supportContact;
    }

    public event Action? Changed;

    /// <summary>The provider that was chosen as primary during setup (never changes at runtime).</summary>
    public string PrimaryProviderId { get; }

    /// <summary>The provider chosen as standby during setup, or null if no standby was configured.</summary>
    public string? SecondaryProviderId { get; }

    /// <summary>The provider currently serving reads and writes. May differ from PrimaryProviderId during failover.</summary>
    public string ActiveProviderId
    {
        get { lock (_sync) return _activeProviderId; }
    }

    /// <summary>
    /// The EF Core provider name string (e.g. "Microsoft.EntityFrameworkCore.SqlServer") for the
    /// active plugin. Used by <see cref="CompanyEmployeesDbContext"/> to validate that the
    /// DbContext's configured provider matches the currently active plugin without hardcoding
    /// provider ID strings such as "postgresql" or "sqlserver".
    /// </summary>
    public string ActiveEfProviderName
    {
        get { lock (_sync) return _activeEfProviderName; }
    }

    public bool PrimaryAvailable
    {
        get { lock (_sync) return _primaryAvailable; }
    }

    public bool SecondaryAvailable
    {
        get { lock (_sync) return _secondaryAvailable; }
    }

    public string? FailoverReason
    {
        get { lock (_sync) return _failoverReason; }
    }

    public string SupportContact { get; }

    /// <summary>True when the active provider is not the configured primary.</summary>
    public bool IsFailoverActive => ActiveProviderId != PrimaryProviderId;

    public int PendingReplicationChanges
    {
        get { lock (_sync) return _pendingReplicationChanges; }
    }

    public DateTime? OldestPendingChangeUtc
    {
        get { lock (_sync) return _oldestPendingChangeUtc; }
    }

    public DateTime? LastSynchronizedUtc
    {
        get { lock (_sync) return _lastSynchronizedUtc; }
    }

    public string? ReplicationError
    {
        get { lock (_sync) return _replicationError; }
    }

    public void UpdateAvailability(bool primaryAvailable, bool secondaryAvailable, string? primaryFailure)
    {
        var changed = false;
        lock (_sync)
        {
            changed = _primaryAvailable != primaryAvailable
                || _secondaryAvailable != secondaryAvailable
                || _failoverReason != primaryFailure;
            _primaryAvailable = primaryAvailable;
            _secondaryAvailable = secondaryAvailable;
            _failoverReason = primaryFailure;
        }

        if (changed)
            Changed?.Invoke();
    }

    public void UpdateReplication(
        int pendingChanges,
        DateTime? oldestPendingChangeUtc,
        DateTime? lastSynchronizedUtc,
        string? error)
    {
        lock (_sync)
        {
            _pendingReplicationChanges = pendingChanges;
            _oldestPendingChangeUtc = oldestPendingChangeUtc;
            _lastSynchronizedUtc = lastSynchronizedUtc;
            _replicationError = error;
        }
        Changed?.Invoke();
    }

    internal void SelectProvider(string providerId, string efProviderName)
    {
        var changed = false;
        lock (_sync)
        {
            changed = _activeProviderId != providerId;
            _activeProviderId = providerId;
            _activeEfProviderName = efProviderName;
        }

        if (changed)
            Changed?.Invoke();
    }
}
