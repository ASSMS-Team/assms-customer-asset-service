using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CustomerAssetService.Security;

public sealed class InternalServiceAuthenticationOptions
{
    public const string SectionName = "InternalServiceAuthentication";
    public string Key { get; set; } = string.Empty;
}

public static class InternalServiceAuthenticationDefaults
{
    public const string Scheme = "InternalServiceKey";
    public const string HeaderName = "X-ASSMS-Service-Key";
    public const string Role = "InternalService";
}

public sealed class InternalServiceKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly InternalServiceAuthenticationOptions _internalOptions;

    public InternalServiceKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder, IOptions<InternalServiceAuthenticationOptions> internalOptions)
        : base(options, logger, encoder) => _internalOptions = internalOptions.Value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(InternalServiceAuthenticationDefaults.HeaderName, out var supplied))
            return Task.FromResult(AuthenticateResult.NoResult());
        var expected = Encoding.UTF8.GetBytes(_internalOptions.Key);
        var actual = Encoding.UTF8.GetBytes(supplied.ToString());
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            return Task.FromResult(AuthenticateResult.Fail("Invalid internal service credential."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "job-service"), new Claim(ClaimTypes.Role, InternalServiceAuthenticationDefaults.Role)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
