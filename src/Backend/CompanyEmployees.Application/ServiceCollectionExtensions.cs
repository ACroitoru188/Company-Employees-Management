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
            services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();
            services.AddScoped<INotificationGateway, NotificationGateway>();
            services.AddScoped<NotificationContext>();
            services.AddScoped<ImpersonationContext>();
            services.AddScoped<DelegationGuard>();
            services.AddScoped<LeaveContext>();
            services.AddScoped<OrgChartContext>();
            services.AddScoped<SearchContext>();
            services.AddScoped<AdminContext>();
            services.AddScoped<EmployeeContext>();
            services.AddScoped<ManagerContext>();

            return services;
        }
    }
}
