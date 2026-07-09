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
        private readonly IEmployeeGateway _employeeGateway;
        private readonly IPasswordHasher _passwordHasher;

        public AuthenticationContext(CompanyEmployeesDbContext dbContext,
            ILogger<AuthenticationContext> logger,
            IEmployeeGateway employeeGateway,
            IPasswordHasher passwordHasher) : base(dbContext, logger)
        {
            _employeeGateway = employeeGateway;
            _passwordHasher = passwordHasher;
        }

        public async Task<LoginResult> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new UnauthorizedException("Invalid credentials");

            var employee = await _employeeGateway.GetEmployeeByEmailAsync(email);

            if (employee == null)
            {
                _logger.LogWarning("Login failed for {Email}: User not found.", email);
                throw new UnauthorizedException("Invalid credentials");
            }
            if (!employee.IsActive)
            {
                _logger.LogWarning("Login failed for {Email}: Account is inactive.", email);
                throw new UnauthorizedException("Invalid credentials");
            }
            var isPasswordValid = _passwordHasher.VerifyPassword(password, employee.PasswordHash);

            if (!isPasswordValid)
            {
                _logger.LogWarning("Login failed for {Email}: Wrong password.", email);
                throw new UnauthorizedException("Invalid credentials");
            }

            _logger.LogInformation("User {Email} logged in successfully.", email);

            return new LoginResult
            {
                EmployeeId = employee.EmployeeId,
                Email = employee.Email,
                FullName = $"{employee.FirstName} {employee.LastName}",
                RoleId = employee.Roles.FirstOrDefault()?.RoleId ?? 0
            };
        }
    }
}
