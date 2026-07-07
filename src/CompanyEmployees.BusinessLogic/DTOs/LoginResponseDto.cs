using CompanyEmployees.Data.Entities;

namespace CompanyEmployees.BusinessLogic.DTOs
{
    public class LoginResponseDto
    {
        public string Token { get; set; } = null!;

        public string Email { get; set; } = null!;

        public Role UserRole { get; set; } = null!;

        public DateTime Expiration { get; set; }
    }
}
