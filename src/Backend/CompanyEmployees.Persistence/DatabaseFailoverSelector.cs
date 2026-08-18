using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CompanyEmployees.Persistence;

public static class DatabaseFailoverSelector
{
    public static async Task<DatabaseRuntimeState> SelectAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var supportContact = configuration["DatabaseFailover:SupportContact"] ?? "1234-23124";
        _ = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        var forcedProvider = configuration["DatabaseFailover:ForceProvider"];
        if (string.Equals(forcedProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            await ProbePostgreSqlAsync(configuration, cancellationToken);
            return new(DatabaseProvider.PostgreSql, false, supportContact,
                "PostgreSQL was selected through DatabaseFailover:ForceProvider.", true);
        }

        if (string.Equals(forcedProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await ProbeSqlServerAsync(configuration, cancellationToken);
            return new(DatabaseProvider.SqlServer, true, supportContact);
        }

        try
        {
            await ProbeSqlServerAsync(configuration, cancellationToken);
            return new(DatabaseProvider.SqlServer, true, supportContact);
        }
        catch (Exception primaryException) when (
            primaryException is SqlException or TimeoutException or OperationCanceledException)
        {
            if (!bool.TryParse(configuration["DatabaseFailover:Enabled"], out var enabled) || !enabled)
                throw new InvalidOperationException(
                    "SQL Server is unavailable and PostgreSQL failover is disabled.", primaryException);

            try
            {
                await ProbePostgreSqlAsync(configuration, cancellationToken);
            }
            catch (Exception fallbackException) when (
                fallbackException is NpgsqlException or TimeoutException or OperationCanceledException)
            {
                throw new InvalidOperationException(
                    "SQL Server and the PostgreSQL fallback are both unavailable.",
                    new AggregateException(primaryException, fallbackException));
            }

            return new(DatabaseProvider.PostgreSql, false, supportContact,
                $"{primaryException.GetType().Name}: {primaryException.Message}", true);
        }
    }

    public static async Task ProbeSqlServerAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = ProbeTimeout(configuration),
            // Probe the server rather than the application catalog. On a fresh Docker
            // volume CompanyEmployees does not exist until EF applies its migrations.
            InitialCatalog = "master"
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
    }

    public static async Task ProbePostgreSqlAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is not configured.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = ProbeTimeout(configuration)
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
    }

    private static int ProbeTimeout(IConfiguration configuration) =>
        Math.Clamp(
            int.TryParse(configuration["DatabaseFailover:ProbeTimeoutSeconds"], out var seconds)
                ? seconds
                : 3,
            1,
            30);
}
