using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

public sealed class CreateTechnicianAccountRequest
{
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,100}$")]
    public string Username { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required, MinLength(12), MaxLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LinkTechnicianAccountRequest
{
    [Required, MaxLength(254)]
    public string Identifier { get; set; } = string.Empty;
}

public sealed record TechnicianAccountResponse(string Id, string Username, string Email, string TechnicianId);
