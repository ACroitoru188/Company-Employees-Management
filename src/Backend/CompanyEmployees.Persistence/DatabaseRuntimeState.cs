namespace CompanyEmployees.Persistence;

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql
}

/// <summary>
/// Process-wide database status. It lives outside either database so the admin can still see
/// and change provider while SQL Server is unavailable.
/// </summary>
public sealed class DatabaseRuntimeState
{
    private readonly object _sync = new();
    private DatabaseProvider _activeProvider;
    private bool _primaryAvailable;
    private bool _postgreSqlAvailable;
    private string? _failoverReason;

    public DatabaseRuntimeState(
        DatabaseProvider activeProvider,
        bool primaryAvailable,
        string supportContact,
        string? failoverReason = null,
        bool postgreSqlAvailable = false)
    {
        _activeProvider = activeProvider;
        _primaryAvailable = primaryAvailable;
        _postgreSqlAvailable = postgreSqlAvailable;
        _failoverReason = failoverReason;
        SupportContact = supportContact;
    }

    public event Action? Changed;

    public DatabaseProvider ActiveProvider
    {
        get { lock (_sync) return _activeProvider; }
    }

    public bool PrimaryAvailable
    {
        get { lock (_sync) return _primaryAvailable; }
    }

    public bool PostgreSqlAvailable
    {
        get { lock (_sync) return _postgreSqlAvailable; }
    }

    public string? FailoverReason
    {
        get { lock (_sync) return _failoverReason; }
    }

    public string SupportContact { get; }
    public bool IsFailoverActive => ActiveProvider == DatabaseProvider.PostgreSql;

    public void UpdateAvailability(
        bool primaryAvailable,
        bool postgreSqlAvailable,
        string? primaryFailure)
    {
        var changed = false;
        lock (_sync)
        {
            changed = _primaryAvailable != primaryAvailable
                || _postgreSqlAvailable != postgreSqlAvailable
                || _failoverReason != primaryFailure;
            _primaryAvailable = primaryAvailable;
            _postgreSqlAvailable = postgreSqlAvailable;
            _failoverReason = primaryFailure;
        }

        if (changed)
            Changed?.Invoke();
    }

    internal void SelectProvider(DatabaseProvider provider)
    {
        var changed = false;
        lock (_sync)
        {
            changed = _activeProvider != provider;
            _activeProvider = provider;
        }

        if (changed)
            Changed?.Invoke();
    }
}
