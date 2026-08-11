using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Infrastructure.Email;
using CompanyEmployees.Infrastructure.Holidays;

namespace CompanyEmployees.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureLayer(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddHttpClient<IPublicHolidayProvider, NagerDatePublicHolidayProvider>(client =>
            {
                client.BaseAddress = new Uri("https://date.nager.at/api/v3/");
                client.Timeout = TimeSpan.FromSeconds(8);
            });

            // Configuration arrives here for the same reason it does in AddPersistenceLayer: the
            // mail server is deployment detail, not code.
            services.Configure<NotificationEmailOptions>(
                configuration.GetSection(NotificationEmailOptions.SectionName));
            services.AddSingleton<INotificationEmailSender, SmtpNotificationEmailSender>();

            return services;
        }
    }
}
