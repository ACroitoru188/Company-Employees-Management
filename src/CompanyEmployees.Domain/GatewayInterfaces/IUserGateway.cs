using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IUserGateway
    {
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<User?> GetUserByEmailAsync(string email);

        Task<List<User>> GetAllUsersAsync();

        Task CreateUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(Guid userId);
    }
}
