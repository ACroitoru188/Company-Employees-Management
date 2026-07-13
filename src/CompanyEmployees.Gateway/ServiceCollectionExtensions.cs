using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Gateway.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Gateway
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGatewayLayer(this IServiceCollection services)
        {
            services.AddScoped<IUserGateway, UserRepository>();
            services.AddScoped<ILeaveRequestGateway, LeaveRequestRepository>();
            services.AddScoped<IUserSessionGateway, UserSessionRepository>();
            return services;
        }
    }
}
