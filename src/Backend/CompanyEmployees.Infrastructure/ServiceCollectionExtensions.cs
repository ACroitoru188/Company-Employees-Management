using Microsoft.Extensions.DependencyInjection;

namespace CompanyEmployees.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
        {
            return services;
        }
    }
}
