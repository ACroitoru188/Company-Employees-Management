using CompanyEmployees.BusinessLogic.DTOs;

namespace CompanyEmployees.BusinessLogic.Interfaces
{
    public interface IAuthenticationService
    {
        Task<LoginResponseDto> LoginAsync(LoginRequestDto loginRequestDto);
    }
}
