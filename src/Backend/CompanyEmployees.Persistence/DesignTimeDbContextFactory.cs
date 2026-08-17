using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Persistence
{
    // Used only by `dotnet ef` design-time tooling. The Web app registers a different
    // (old) DbContext, so EF can't discover this one from a startup project yet.
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CompanyEmployeesDbContext>
    {
        // LocalDB is Windows-only, so it can only be the default, never the sole option: on
        // Linux or macOS point ConnectionStrings__Default at a SQL Server instance (the mssql
        // Docker image, say) before running any `dotnet ef` command.
        private const string LocalDbFallback =
            "Server=(localdb)\\MSSQLLocalDB;Database=CompanyEmployees;Trusted_Connection=True;MultipleActiveResultSets=true";

        public CompanyEmployeesDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = LocalDbFallback;

            var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new CompanyEmployeesDbContext(options);
        }
    }
}
