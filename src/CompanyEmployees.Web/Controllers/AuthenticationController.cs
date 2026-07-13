using System.ComponentModel.DataAnnotations;
using CompanyEmployees.Application.Contexts;
using CompanyEmployees.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CompanyEmployees.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class 
    AuthenticationController : ControllerBase
{
    private readonly AuthenticationContext _authentication;

    public AuthenticationController(AuthenticationContext authentication)
    {
        _authentication = authentication;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers.UserAgent.ToString();

            var result = await _authentication.LoginAsync(request.Email, request.Password, ipAddress, userAgent);
            return Ok(result);
        }
        catch (UnauthorizedException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    public record LoginRequest(
        [property: Required, EmailAddress] string Email,
        [property: Required] string Password);
}
