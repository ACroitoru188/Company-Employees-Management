using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Persistence
{
    // Used only by `dotnet ef` design-time tooling. The Web app registers a different
    // (old) DbContext, so EF can't discover this one from a startup project yet.
    // ponytail: hardcoded LocalDB string, mirrors the old Data project's factory.
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CompanyEmployeesDbContext>
    {
        public CompanyEmployeesDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<CompanyEmployeesDbContext>()
                .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=CompanyEmployees;Trusted_Connection=True;MultipleActiveResultSets=true")
                .Options;

            return new CompanyEmployeesDbContext(options);
        }
    }
}
