using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CustomerAssetService.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerAssetService.Security;

public static class StaffAccountTokenValidator
{
    public static async Task ValidateTokenAccountActiveAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(sub))
        {
            context.Fail("Missing subject claim in token.");
            return;
        }

        var repo = context.HttpContext.RequestServices.GetRequiredService<IStaffAccountRepository>();
        var account = await repo.FindByIdAsync(sub);
        if (account is null || !string.Equals(account.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            context.Fail("Staff account is deactivated or no longer active.");
        }
    }
}
