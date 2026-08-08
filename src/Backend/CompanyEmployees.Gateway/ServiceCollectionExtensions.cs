using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Gateway.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyEmployees.Gateway
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGatewayLayer(this IServiceCollection services)
        {
            services.AddScoped<IUserGateway, UserRepository>();
            services.AddScoped<ILeaveRequestGateway, LeaveRequestRepository>();
            services.AddScoped<IDepartmentGateway, DepartmentRepository>();
            services.AddScoped<IRegionGateway, RegionRepository>();
            services.AddScoped<IContractGateway, ContractRepository>();
            services.AddScoped<IManagerDelegationGateway, ManagerDelegationRepository>();
            services.AddScoped<IImpersonationGateway, ImpersonationRepository>();
            return services;
        }
    }
}
