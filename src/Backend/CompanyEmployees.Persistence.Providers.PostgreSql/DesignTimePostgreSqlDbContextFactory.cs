using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Persistence.Providers.PostgreSql;

// Used only by `dotnet ef` design-time tooling (migrations add/update/remove).
// Run dotnet ef commands with:
//   --project src/Backend/CompanyEmployees.Persistence.Providers.PostgreSql
//   --startup-project src/Backend/CompanyEmployees.Persistence.Providers.PostgreSql
// Override the connection string via the ConnectionStrings__PostgreSql or ConnectionStrings__Default environment variable.
public sealed class DesignTimePostgreSqlDbContextFactory : IDesignTimeDbContextFactory<CompanyEmployeesDbContext>
{
    // Matches the development PostgreSQL service in compose.yaml.
    private const string DockerPostgreSqlFallback =
        "Host=localhost;Port=5432;Database=company_employees;Username=company_app;Password=company_dev_password";

    public CompanyEmployeesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DockerPostgreSqlFallback;

        var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(DesignTimePostgreSqlDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new CompanyEmployeesDbContext(options);
    }
}
