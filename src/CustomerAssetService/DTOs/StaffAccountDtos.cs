using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

public sealed class CreateStaffAccountRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z0-9._-]{3,100}$",
        ErrorMessage = "Username must be 3–100 characters: letters, digits, dots, hyphens or underscores.")]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression("^(Agent|Dispatcher|Technician|Manager)$",
        ErrorMessage = "Role must be Agent, Dispatcher, Technician or Manager.")]
    public string Role { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Required when Role is Technician. The Dispatch technician UUID to link.</summary>
    public string? TechnicianId { get; set; }
}

/// <summary>The response for a created staff account. Never exposes PasswordHash.</summary>
public sealed record StaffAccountResponse(
    string Id,
    string FullName,
    string Username,
    string Email,
    string Role,
    string Status,
    string? TechnicianId,
    DateTime CreatedAt,
    string? CreatedBy,
    string? DispatchSyncStatus = null);
