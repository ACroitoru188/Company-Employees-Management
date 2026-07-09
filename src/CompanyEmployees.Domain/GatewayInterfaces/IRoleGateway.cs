using CompanyEmployees.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IRoleGateway
    {
        Task<Role?> GetRoleByIdAsync(int roleId);
        Task<List<Role>> GetAllRolesAsync();
    }
}
