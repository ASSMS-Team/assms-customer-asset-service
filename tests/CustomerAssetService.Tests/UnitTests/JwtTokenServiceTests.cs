using System.IdentityModel.Tokens.Jwt;

using CustomerAssetService.Models;
using CustomerAssetService.Security;
using Microsoft.Extensions.Options;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class JwtTokenServiceTests
{
    [Fact]
    public void Issue_ContainsIdentityRoleAndExpiry()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "issuer",
            Audience = "audience",
            SigningKey = "a-test-signing-key-that-is-at-least-32-bytes",
            LifetimeMinutes = 60
        });
        var service = new JwtTokenService(options, TimeProvider.System);
        var account = new StaffAccount
        {
            Id = "staff-1", Username = "dispatcher", Email = "d@example.com",
            PasswordHash = "not-returned", Role = StaffRoles.Dispatcher, Status = "ACTIVE"
        };

        var response = service.Issue(account);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);

        Assert.Equal("staff-1", token.Subject);
        Assert.Contains(token.Claims, claim => claim.Type == "role" && claim.Value == StaffRoles.Dispatcher);
        Assert.True(response.ExpiresAt > DateTime.UtcNow.AddMinutes(59));
        Assert.DoesNotContain(account.PasswordHash, response.AccessToken, StringComparison.Ordinal);
    }
}
