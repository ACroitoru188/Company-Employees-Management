using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace CompanyEmployees.Persistence
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPersistenceLayer(
            this IServiceCollection services,
            IConfiguration configuration,
            DatabaseRuntimeState databaseState)
        {
            services.AddSingleton(databaseState);
            services.AddSingleton<PostgreSqlStandbySynchronizer>();
            services.AddSingleton<DatabaseProviderSwitcher>();
            services.AddDbContext<CompanyEmployeesDbContext>(options =>
            {
                if (databaseState.ActiveProvider == DatabaseProvider.PostgreSql)
                {
                    options.UseNpgsql(configuration.GetConnectionString("PostgreSql"));
                    return;
                }

                options.UseSqlServer(configuration.GetConnectionString("Default"));
            });

            return services;
        }
    }
}
