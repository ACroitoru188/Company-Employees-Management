using Microsoft.Extensions.DependencyInjection;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Infrastructure.Holidays;

namespace CompanyEmployees.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
        {
            services.AddHttpClient<IPublicHolidayProvider, NagerDatePublicHolidayProvider>(client =>
            {
                client.BaseAddress = new Uri("https://date.nager.at/api/v3/");
                client.Timeout = TimeSpan.FromSeconds(8);
            });
            return services;
        }
    }
}
