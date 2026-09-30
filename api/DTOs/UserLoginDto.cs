using System.ComponentModel.DataAnnotations;

namespace ApiInsightStudio.Api.DTOs;

public class UserLoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
}
