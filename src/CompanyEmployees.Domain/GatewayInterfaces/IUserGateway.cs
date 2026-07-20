using CompanyEmployees.Domain.Entities;

namespace CompanyEmployees.Domain.GatewayInterfaces
{
    public interface IUserGateway
    {
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<User?> GetUserByEmailAsync(string email);

        Task<List<User>> GetAllUsersAsync();

        // Active direct reports of a manager (User.ManagerId). User.DirectReports is never
        // loaded by GetUserByIdAsync, so the query has to go the other way round.
        Task<List<User>> GetDirectReportsAsync(Guid managerId);

        Task CreateUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task DeleteUserAsync(Guid userId);
    }
}
