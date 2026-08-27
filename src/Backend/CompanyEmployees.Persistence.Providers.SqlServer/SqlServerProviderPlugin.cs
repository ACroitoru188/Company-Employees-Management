using CompanyEmployees.Persistence.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence.Providers.SqlServer;

/// <summary>
/// Runtime plugin that configures CompanyEmployees to use Microsoft SQL Server.
/// </summary>
public sealed class SqlServerProviderPlugin : IDbProviderPlugin
{
    public string Id => "sqlserver";
    public string DisplayName => "Microsoft SQL Server";

    public IReadOnlyList<ConnectionField> RequiredFields =>
    [
        new("Server",                "Server / host",              IsSecret: false, DefaultValue: "localhost,1433"),
        new("Database",              "Database name",              IsSecret: false, DefaultValue: "CompanyEmployees"),
        new("User Id",               "Username",                   IsSecret: false, DefaultValue: "sa"),
        new("Password",              "Password",                   IsSecret: true),
        new("TrustServerCertificate","Trust server certificate",   IsSecret: false, DefaultValue: "True"),
        new("MultipleActiveResultSets", "Multiple Active Result Sets", IsSecret: false, DefaultValue: "true"),
    ];

    public void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(
            connectionString,
            sql => sql.MigrationsAssembly(typeof(SqlServerProviderPlugin).Assembly.GetName().Name));

    /// <inheritdoc />
    public async Task TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 5,
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
    }

    /// <inheritdoc />
    public async Task ApplyMigrationsAsync(DbContext context, CancellationToken ct = default) =>
        await context.Database.MigrateAsync(ct);
}
