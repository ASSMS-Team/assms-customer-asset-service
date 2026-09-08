using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

public sealed class LoginRequest
{
    [Required, StringLength(255)]
    public string Identifier { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Password { get; init; } = string.Empty;
}
