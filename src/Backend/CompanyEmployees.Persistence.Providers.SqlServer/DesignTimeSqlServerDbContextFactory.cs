using CompanyEmployees.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Persistence.Providers.SqlServer;

// Used only by `dotnet ef` design-time tooling (migrations add/update/remove).
// Run dotnet ef commands with:
//   --project src/Backend/CompanyEmployees.Persistence.Providers.SqlServer
//   --startup-project src/Backend/CompanyEmployees.Persistence.Providers.SqlServer
// Override the connection string via the ConnectionStrings__Default environment variable.
public sealed class DesignTimeSqlServerDbContextFactory : IDesignTimeDbContextFactory<CompanyEmployeesDbContext>
{
    // Matches the development SQL Server service in compose.yaml.
    private const string DockerSqlServerFallback =
        "Server=localhost,1433;Database=CompanyEmployees;User Id=sa;Password=CompanyEmployees_dev_2026!;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public CompanyEmployeesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = DockerSqlServerFallback;

        var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(DesignTimeSqlServerDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new CompanyEmployeesDbContext(options);
    }
}
