using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CompanyEmployees.BusinessLogic.DTOs;
using CompanyEmployees.BusinessLogic.Interfaces;
using CompanyEmployees.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CompanyEmployees.BusinessLogic.Implementations;

public class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<Employee> _userManager;
    private readonly IConfiguration _configuration;

    public AuthenticationService(UserManager<Employee> userManager, IConfiguration configuration)
    {
        _userManager = userManager;
        _configuration = configuration;
    }

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto loginRequestDto)
    {
        var user = await _userManager.FindByEmailAsync(loginRequestDto.Email);
        if (user == null)
        {
            throw new UnauthorizedAccessException("Credențiale invalide");
        }

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, loginRequestDto.Password);
        if (!isPasswordValid)
        {
            throw new UnauthorizedAccessException("Credențiale invalide");
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var secretKey = _configuration.GetSection("JwtSettings:Secret").Value;
        if (string.IsNullOrEmpty(secretKey))
        {
            throw new InvalidOperationException("JwtSettings:Secret is missing from configuration.");
        }

        var key = Encoding.UTF8.GetBytes(secretKey);
        
        var userRoles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in userRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return new LoginResponseDto
        {
            Token = tokenHandler.WriteToken(token),
            Email = user.Email!,
            Expiration = tokenDescriptor.Expires.Value
        };
    }
}
