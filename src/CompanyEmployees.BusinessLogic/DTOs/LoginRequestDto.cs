using System.ComponentModel.DataAnnotations;

namespace CompanyEmployees.BusinessLogic.DTOs
{
    public class LoginRequestDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;
    }
}
