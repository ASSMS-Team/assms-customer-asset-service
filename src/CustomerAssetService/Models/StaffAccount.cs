namespace CustomerAssetService.Models;

public sealed class StaffAccount
{
    public required string Id { get; init; }
    public required string FullName { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required string Role { get; init; }
    public required string Status { get; set; }
    public string? TechnicianId { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? DispatchSyncStatus { get; set; }
}

