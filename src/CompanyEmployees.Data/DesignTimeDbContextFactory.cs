using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CompanyEmployees.Data;

/// <summary>
/// Folosit doar de tooling-ul EF Core (dotnet ef) la design time, cât timp
/// nu există un proiect de startup care să configureze DbContext-ul prin DI.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=CompanyEmployees;Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;

        return new ApplicationDbContext(options);
    }
}
