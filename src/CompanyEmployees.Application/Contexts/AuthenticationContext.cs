using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Infrastructure.Security;
using CompanyEmployees.Persistence;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Application.Contexts
{
    public class AuthenticationContext : BaseContext
    {
        private readonly IUserGateway _userGateway;
        private readonly IPasswordHasher _passwordHasher;

        public AuthenticationContext(CompanyEmployeesDbContext dbContext,
            ILogger<AuthenticationContext> logger,
            IUserGateway userGateway,
            IPasswordHasher passwordHasher) : base(dbContext, logger)
        {
            _userGateway = userGateway;
            _passwordHasher = passwordHasher;
        }

        public async Task<LoginResult> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new UnauthorizedException("Invalid credentials");

            var user = await _userGateway.GetUserByEmailAsync(email);

            if (user == null)
            {
                _logger.LogWarning("Login failed for {Email}: User not found.", email);
                throw new UnauthorizedException("Invalid credentials");
            }
            if (user.Status != UserStatus.Active)
            {
                _logger.LogWarning("Login failed for {Email}: Account is inactive.", email);
                throw new UnauthorizedException("Invalid credentials");
            }
            var isPasswordValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);

            if (!isPasswordValid)
            {
                _logger.LogWarning("Login failed for {Email}: Wrong password.", email);
                throw new UnauthorizedException("Invalid credentials");
            }

            _logger.LogInformation("User {Email} logged in successfully.", email);

            return new LoginResult
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.Name,
                Role = user.Role
            };
        }
    }
}
