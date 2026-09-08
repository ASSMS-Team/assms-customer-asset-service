namespace CustomerAssetService.DTOs;

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    StaffIdentity Staff);

public sealed record StaffIdentity(string Id, string Username, string Email, string Role);
