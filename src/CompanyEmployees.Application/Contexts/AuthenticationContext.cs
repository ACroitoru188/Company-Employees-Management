using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Exceptions;
using CompanyEmployees.Domain.GatewayInterfaces;
using CompanyEmployees.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Application.Contexts
{
    public class AuthenticationContext : BaseContext
    {
        private readonly IUserGateway _userGateway;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IUserSessionGateway _userSessionGateway;

        public AuthenticationContext(
            ILogger<AuthenticationContext> logger,
            IUserGateway userGateway,
            IPasswordHasher passwordHasher,
            IUserSessionGateway userSessionGateway) : base(logger)
        {
            _userGateway = userGateway;
            _passwordHasher = passwordHasher;
            _userSessionGateway = userSessionGateway;
        }

        private static string GenerateSecureToken()
        {
            var bytes = new byte[64];
            RandomNumberGenerator.Create().GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public async Task<LoginResult> LoginAsync(string email, string password, string ipAddress, string? userAgent)
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

            var session = new UserSession
            {
                UserId = user.Id,
                LoginTime = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                ExpiryTime = DateTime.UtcNow.AddHours(8),
                IsActive = true,
                SessionToken = GenerateSecureToken(),
            };

            await _userSessionGateway.CreateSessionAsync(session);

            return new LoginResult
            {
                UserId = user.Id,
                Email = user.Email,
                FullName = user.Name,
                Role = user.Role,
                SessionToken = session.SessionToken
            };
        }
    }
}
