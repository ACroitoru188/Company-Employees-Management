using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IDepartmentGateway
    {
        Task<List<Department>> GetAllAsync();          // include manager + membri
        Task<Department?> GetByIdAsync(Guid id);
        Task CreateAsync(Department department);
        Task UpdateAsync(Department department);
        Task DeleteAsync(Guid id);                     // stergere definitiva; membrii devin null prin setnull
    }
}
