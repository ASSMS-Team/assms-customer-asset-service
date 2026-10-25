using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using CustomerAssetService.Controllers;
using CustomerAssetService.DTOs;
using CustomerAssetService.Extensions;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class SecurityAndDefaultsMutationTests
{
    // ---------- Default values of DTOs, models and options ----------

    public static IEnumerable<object[]> DefaultConstructibleTypes() =>
        typeof(CustomerService).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }
                        && (t.Namespace is "CustomerAssetService.DTOs" or "CustomerAssetService.Models"
                            || t == typeof(InternalServiceAuthenticationOptions))
                        && t.GetConstructor(Type.EmptyTypes) is not null
                        // Types with required members have no defaults to check.
                        && !t.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute"))
            .Select(t => new object[] { t.FullName! });

    // A request or model that is created without a value for a non-nullable
    // string must hold "", never a placeholder, so validation and mapping see
    // a real empty value.
    [Theory]
    [MemberData(nameof(DefaultConstructibleTypes))]
    public void NonNullableStringProperties_DefaultToEmpty(string typeName)
    {
        var type = typeof(CustomerService).Assembly.GetType(typeName)!;
        var instance = Activator.CreateInstance(type)!;
        var nullability = new NullabilityInfoContext();

        foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanRead))
        {
            var value = (string?)property.GetValue(instance);
            if (nullability.Create(property).ReadState == NullabilityState.NotNull)
                Assert.True(value == string.Empty, $"{type.Name}.{property.Name} should default to an empty string but was '{value}'.");
            else
                Assert.Null(value);
        }
    }

    [Fact]
    public void JwtOptions_DefaultToTheSharedIssuerAndAudience_AndNoKey()
    {
        var options = new JwtOptions();

        Assert.Equal("assms-customer-asset-service", options.Issuer);
        Assert.Equal("assms-internal", options.Audience);
        Assert.Equal(string.Empty, options.SigningKey);
    }

    // ---------- PasswordHasher ----------

    private static readonly PasswordHasher Hasher = new();

    private static string Encode(string password, int iterations, int saltSize = 16, int keySize = 32, string prefix = "pbkdf2-sha256")
    {
        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, keySize);
        return $"{prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_RejectsMissingPasswords(string? password)
    {
        Assert.ThrowsAny<ArgumentException>(() => Hasher.Hash(password!));
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void Verify_AcceptsTheIterationBoundaries(int iterations)
    {
        Assert.True(Hasher.Verify("Correct!Pass1", Encode("Correct!Pass1", iterations)));
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(1_000_001)]
    public void Verify_RejectsIterationCountsOutsideTheAllowedRange(int iterations)
    {
        Assert.False(Hasher.Verify("Correct!Pass1", Encode("Correct!Pass1", iterations)));
    }

    [Fact]
    public void Verify_RejectsAnotherAlgorithmPrefix_EvenWithAMatchingKey()
    {
        Assert.False(Hasher.Verify("Correct!Pass1", Encode("Correct!Pass1", 210_000, prefix: "pbkdf2-sha1")));
    }

    [Fact]
    public void Verify_RejectsAKeyOfTheWrongLength_EvenWhenItMatches()
    {
        Assert.False(Hasher.Verify("Correct!Pass1", Encode("Correct!Pass1", 210_000, keySize: 31)));
    }

    [Fact]
    public void Verify_RejectsASaltOfTheWrongLength_EvenWhenItMatches()
    {
        Assert.False(Hasher.Verify("Correct!Pass1", Encode("Correct!Pass1", 210_000, saltSize: 8)));
    }

    [Theory]
    [InlineData("pbkdf2-sha256$210000$not-base64!$also-not")]
    [InlineData("pbkdf2-sha256$210000$abc")]
    [InlineData("pbkdf2-sha256$lots$AAAA$BBBB")]
    public void Verify_RejectsMalformedHashes(string encoded)
    {
        Assert.False(Hasher.Verify("Correct!Pass1", encoded));
    }

    // ---------- Internal service key authentication ----------

    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private static async Task<AuthenticateResult> AuthenticateInternal(string? suppliedKey)
    {
        var handler = new InternalServiceKeyAuthenticationHandler(new StaticOptionsMonitor(), NullLoggerFactory.Instance,
            UrlEncoder.Default, Options.Create(new InternalServiceAuthenticationOptions { Key = "internal-service-key-of-32-bytes!!" }));
        var http = new DefaultHttpContext();
        if (suppliedKey is not null) http.Request.Headers[InternalServiceAuthenticationDefaults.HeaderName] = suppliedKey;
        await handler.InitializeAsync(new AuthenticationScheme(InternalServiceAuthenticationDefaults.Scheme, null, typeof(InternalServiceKeyAuthenticationHandler)), http);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task InternalKey_NoHeader_ProducesNoResult()
    {
        var result = await AuthenticateInternal(null);

        Assert.True(result.None);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task InternalKey_WrongKey_FailsWithMessage()
    {
        var result = await AuthenticateInternal("wrong-key");

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid internal service credential.", result.Failure!.Message);
    }

    [Fact]
    public async Task InternalKey_CorrectKey_AuthenticatesTheJobServiceWithTheInternalRole()
    {
        var result = await AuthenticateInternal("internal-service-key-of-32-bytes!!");

        Assert.True(result.Succeeded);
        Assert.Equal("job-service", result.Principal!.Identity!.Name);
        Assert.True(result.Principal.IsInRole(InternalServiceAuthenticationDefaults.Role));
        Assert.Equal(InternalServiceAuthenticationDefaults.Scheme, result.Ticket!.AuthenticationScheme);
    }

    // ---------- Token validation: subject claim fallback ----------

    private static TokenValidatedContext TokenContext(FakeStaffAccountRepository repo, params Claim[] claims)
    {
        var services = new ServiceCollection().AddSingleton<IStaffAccountRepository>(repo).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return new TokenValidatedContext(http, new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
    }

    [Fact]
    public async Task TokenValidation_FallsBackToNameIdentifier_WhenThereIsNoSubClaim()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("acc-1"));
        var context = TokenContext(repo, new Claim(ClaimTypes.NameIdentifier, "acc-1"));

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.Null(context.Result?.Failure);
    }

    [Fact]
    public async Task TokenValidation_PrefersTheSubClaim_OverNameIdentifier()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("acc-1"));
        repo.Accounts.Add(FakeStaffAccountRepository.Account("acc-2", status: "INACTIVE"));
        var context = TokenContext(repo, new Claim(JwtRegisteredClaimNames.Sub, "acc-1"), new Claim(ClaimTypes.NameIdentifier, "acc-2"));

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.Null(context.Result?.Failure);
    }

    // ---------- AuthController and token response ----------

    [Fact]
    public async Task Login_Failure_ReturnsATitledUnauthorizedProblem()
    {
        var service = new CustomerAssetService.Services.AuthenticationService(new FakeStaffAccountRepository(), Hasher,
            new JwtTokenService(Options.Create(new JwtOptions { SigningKey = "a-test-signing-key-that-is-at-least-32-bytes" }), TimeProvider.System));

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(await new AuthController(service).Login(new LoginRequest { Identifier = "nobody", Password = "x" }));

        var problem = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal("Authentication failed.", problem.Title);
        Assert.Equal(401, problem.Status);
    }

    [Fact]
    public void Me_ReturnsTheRoleAndTechnicianClaims()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, "staff-1"), new Claim("role", "Technician"), new Claim("technician_id", "tech-9")], "Bearer"))
        };
        var controller = new AuthController(null!) { ControllerContext = new ControllerContext { HttpContext = http } };

        var body = Assert.IsType<OkObjectResult>(controller.Me()).Value!;

        Assert.Equal("staff-1", body.GetType().GetProperty("id")!.GetValue(body));
        Assert.Equal("Technician", body.GetType().GetProperty("role")!.GetValue(body));
        Assert.Equal("tech-9", body.GetType().GetProperty("technicianId")!.GetValue(body));
    }

    [Fact]
    public void IssuedTokens_AreBearerTokens()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = "a-test-signing-key-that-is-at-least-32-bytes" }), TimeProvider.System);

        var response = service.Issue(FakeStaffAccountRepository.Account("staff-1"));

        Assert.Equal("Bearer", response.TokenType);
    }

    // ---------- PhoneNormalizer: both country-code conditions are required ----------

    [Theory]
    [InlineData("12345678901", "12345678901")]
    [InlineData("94123", "94123")]
    public void Normalize_OnlyStripsTheCountryCode_ForAnElevenDigitNumberStartingWith94(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));
    }
}
