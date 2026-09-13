using CompanyEmployees.Application;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Gateway;
using CompanyEmployees.Persistence;
using CompanyEmployees.Persistence.Contracts;
using CompanyEmployees.Persistence.Providers.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Respawn;
using Testcontainers.MsSql;
using Xunit;

namespace CompanyEmployees.Integration.Tests.Fixtures;

public class SqlServerDatabaseFixture : IDatabaseFixture, IAsyncLifetime
{
    private const string LocalDefaultConnectionString =
        "Server=localhost,1433;Database=CompanyEmployees_IntegrationTest;User Id=sa;Password=CompanyEmployees_dev_2026!;TrustServerCertificate=True;MultipleActiveResultSets=true;Connect Timeout=5";

    private const string LocalMasterConnectionString =
        "Server=localhost,1433;Database=master;User Id=sa;Password=CompanyEmployees_dev_2026!;TrustServerCertificate=True;Connect Timeout=3";

    private MsSqlContainer? _container;
    private Respawner? _respawner;
    private IServiceProvider? _serviceProvider;

    public string ConnectionString { get; private set; } = string.Empty;
    public IDbProviderPlugin Plugin { get; } = new SqlServerProviderPlugin();

    public async Task InitializeAsync()
    {
        var configuredConn = Environment.GetEnvironmentVariable("TEST_MSSQL_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configuredConn))
        {
            ConnectionString = configuredConn;
        }
        else if (await CanConnectAsync(LocalMasterConnectionString))
        {
            ConnectionString = LocalDefaultConnectionString;
        }
        else
        {
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        // Apply migrations and create outbox schema
        await using (var context = CreateDbContext())
        {
            await Plugin.ApplyMigrationsAsync(context);
            await Plugin.CreateOutboxSchemaAsync(context);
        }

        // Initialize Respawner for fast reset between tests
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            TablesToIgnore = new Respawn.Graph.Table[] { "__EFMigrationsHistory", "DatabaseOutbox" }
        });

        BuildServiceProvider();
    }

    private void BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPublicHolidayProvider, TestPublicHolidayProvider>();

        var runtimeState = new DatabaseRuntimeState(Plugin.Id, Plugin.Id, Plugin.EfProviderName, true, "test");
        services.AddSingleton(runtimeState);

        services.AddDbContext<CompanyEmployeesDbContext>(options =>
            Plugin.ConfigureDbContext(options, ConnectionString));

        services.AddGatewayLayer();
        services.AddApplicationLayer();

        _serviceProvider = services.BuildServiceProvider();
    }

    public CompanyEmployeesDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<CompanyEmployeesDbContext>();
        Plugin.ConfigureDbContext(optionsBuilder, ConnectionString);
        var runtimeState = new DatabaseRuntimeState(Plugin.Id, Plugin.Id, Plugin.EfProviderName, true, "test");
        return new CompanyEmployeesDbContext(optionsBuilder.Options, null, runtimeState)
        {
            SuppressOutboxCapture = true
        };
    }

    public IServiceScope CreateScope()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Fixture has not been initialized.");
        return _serviceProvider.CreateScope();
    }

    public async Task ResetDatabaseAsync()
    {
        if (_respawner != null)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await _respawner.ResetAsync(connection);
        }
    }

    private static async Task<bool> CanConnectAsync(string connectionString)
    {
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
