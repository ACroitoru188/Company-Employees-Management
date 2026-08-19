using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Persistence
{
    // Used only by `dotnet ef` design-time tooling. The Web app registers a different
    // (old) DbContext, so EF can't discover this one from a startup project yet.
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CompanyEmployeesDbContext>
    {
        // Matches the development SQL Server service in compose.yaml. Override it with
        // ConnectionStrings__Default when using LocalDB, SQL Express, or a remote server.
        private const string DockerSqlServerFallback =
            "Server=localhost,1433;Database=CompanyEmployees;User Id=sa;Password=CompanyEmployees_dev_2026!;TrustServerCertificate=True;MultipleActiveResultSets=true";

        public CompanyEmployeesDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = DockerSqlServerFallback;

            var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new CompanyEmployeesDbContext(options);
        }
    }
}
