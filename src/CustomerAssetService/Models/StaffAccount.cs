namespace CustomerAssetService.Models;

public sealed class StaffAccount
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public required string Role { get; init; }
    public required string Status { get; init; }
}
