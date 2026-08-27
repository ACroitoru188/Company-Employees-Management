using CompanyEmployees.Persistence.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompanyEmployees.Persistence.Providers.PostgreSql;

/// <summary>
/// Runtime plugin that configures CompanyEmployees to use PostgreSQL.
/// </summary>
public sealed class PostgreSqlProviderPlugin : IDbProviderPlugin
{
    public string Id => "postgresql";
    public string DisplayName => "PostgreSQL";

    public IReadOnlyList<ConnectionField> RequiredFields =>
    [
        new("Host",     "Host",          IsSecret: false, DefaultValue: "localhost"),
        new("Port",     "Port",          IsSecret: false, DefaultValue: "5432"),
        new("Database", "Database name", IsSecret: false, DefaultValue: "CompanyEmployees"),
        new("Username", "Username",      IsSecret: false, DefaultValue: "postgres"),
        new("Password", "Password",      IsSecret: true),
    ];

    /// <inheritdoc />
    public void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString);

    /// <inheritdoc />
    public async Task TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
    }

    public async Task ApplyMigrationsAsync(DbContext context, CancellationToken ct = default) =>
        await context.Database.EnsureCreatedAsync(ct);
}
