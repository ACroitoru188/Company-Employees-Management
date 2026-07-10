using CompanyEmployees.Domain.Interfaces;
using CompanyEmployees.Infrastructure.Security;
using CompanyEmployees.Persistence;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services)
        {
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddTransient<DatabaseSeeder>();

            return services;
        }
    }
}
