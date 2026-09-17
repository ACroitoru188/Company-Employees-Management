using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using Microsoft.Extensions.Logging;

namespace CompanyEmployees.Application.Contexts
{
    public class EmployeeContext : BaseContext
    {
        private readonly IUserGateway _userGateway;

        public EmployeeContext(
            ILogger<EmployeeContext> logger,
            IUserGateway userGateway) : base(logger)
        {
            _userGateway = userGateway;
        }

        public async Task<User> GetEmployeeByEmailAsync(string email)
        {
            var user = await _userGateway.GetUserByEmailAsync(email);
            if (user == null)
                throw new EntityNotFoundException($"No user with email {email}.");
            return user;
        }

        public async Task<User> GetEmployeeByIdAsync(Guid userId)
        {
            var user = await _userGateway.GetUserByIdAsync(userId);
            if (user == null)
                throw new EntityNotFoundException($"No user with id {userId}.");
            return user;
        }
    }
}
