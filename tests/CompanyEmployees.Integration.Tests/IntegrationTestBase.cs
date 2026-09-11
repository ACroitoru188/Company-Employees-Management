using CompanyEmployees.Integration.Tests.Fixtures;
using CompanyEmployees.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CompanyEmployees.Integration.Tests;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly IDatabaseFixture Fixture;
    private IServiceScope? _scope;

    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected IServiceProvider Services => _scope?.ServiceProvider
        ?? throw new InvalidOperationException("Test scope has not been initialized.");

    protected CompanyEmployeesDbContext Db => Services.GetRequiredService<CompanyEmployeesDbContext>();

    public virtual async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        _scope = Fixture.CreateScope();
    }

    public virtual Task DisposeAsync()
    {
        _scope?.Dispose();
        return Task.CompletedTask;
    }
}
