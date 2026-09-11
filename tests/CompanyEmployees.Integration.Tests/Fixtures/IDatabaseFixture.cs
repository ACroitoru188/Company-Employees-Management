using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyEmployees.Integration.Tests.Fixtures;

public interface IDatabaseFixture
{
    string ConnectionString { get; }
    IDbProviderPlugin Plugin { get; }
    CompanyEmployeesDbContext CreateDbContext();
    IServiceScope CreateScope();
    Task ResetDatabaseAsync();
}
