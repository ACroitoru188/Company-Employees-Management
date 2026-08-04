using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Application.Notifications;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Gateway.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyEmployees.Application
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
        {
            services.AddScoped<EmployeeContext>();
            // Singleton on purpose: subscribers live in per-circuit components and
            // publishers in scoped contexts, so they only meet if there is one instance.
            services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
            services.AddScoped<INotificationGateway, NotificationGateway>();
            services.AddScoped<NotificationContext>();
            services.AddScoped<ManagerContext>();

            return services;
        }
    }
}
