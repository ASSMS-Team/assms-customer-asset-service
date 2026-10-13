using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class StaffAccountTokenValidatorTests
{
    private sealed class FakeStaffAccountRepository : IStaffAccountRepository
    {
        public List<StaffAccount> Accounts { get; } = [];

        public Task<StaffAccount?> FindByIdAsync(string id) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));

        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.Username == identifier || a.Email == identifier));

        public Task<bool> AnyManagerAsync() =>
            Task.FromResult(Accounts.Any(a => a.Role == "Manager"));

        public Task CreateAsync(StaffAccount account)
        {
            Accounts.Add(account);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync() =>
            Task.FromResult<IReadOnlyList<StaffAccount>>(Accounts.Where(a => a.Status == "ACTIVE").ToList());

        public Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) =>
            Task.FromResult<IReadOnlyList<StaffAccount>>(string.IsNullOrWhiteSpace(status)
                ? Accounts
                : Accounts.Where(a => a.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<DeactivateStaffAccountResult> DeactivateAsync(string id) =>
            Task.FromResult(DeactivateStaffAccountResult.Success);

        public Task<DeactivateStaffAccountResult> DeactivateAsync(string id, string? dispatchSyncStatus) =>
            Task.FromResult(DeactivateStaffAccountResult.Success);

        public Task UpdateDispatchSyncStatusAsync(string id, string status) => Task.CompletedTask;

        public Task<int> CountActiveManagersAsync() =>
            Task.FromResult(Accounts.Count(a => a.Role == "Manager" && a.Status == "ACTIVE"));
    }

    private static TokenValidatedContext CreateContext(ClaimsPrincipal? principal, IStaffAccountRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var options = new JwtBearerOptions();

        var context = new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = principal
        };

        return context;
    }

    [Fact]
    public async Task ValidateToken_ActiveStaffAccount_Succeeds()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(new StaffAccount
        {
            Id = "acc-active",
            FullName = "Active Manager",
            Username = "active.mgr",
            Email = "active@assms.com",
            PasswordHash = "hash",
            Role = "Manager",
            Status = "ACTIVE"
        });

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "acc-active"),
            new Claim(JwtRegisteredClaimNames.UniqueName, "active.mgr"),
            new Claim("role", "Manager")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal, repo);

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.Null(context.Result?.Failure);
    }

    [Fact]
    public async Task ValidateToken_DeactivatedStaffAccount_FailsAuthentication()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(new StaffAccount
        {
            Id = "acc-inactive",
            FullName = "Inactive Manager",
            Username = "inactive.mgr",
            Email = "inactive@assms.com",
            PasswordHash = "hash",
            Role = "Manager",
            Status = "INACTIVE"
        });

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "acc-inactive"),
            new Claim(JwtRegisteredClaimNames.UniqueName, "inactive.mgr"),
            new Claim("role", "Manager")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal, repo);

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.Contains("deactivated", context.Result!.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateToken_NonExistentAccount_FailsAuthentication()
    {
        var repo = new FakeStaffAccountRepository();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "unknown-id"),
            new Claim(JwtRegisteredClaimNames.UniqueName, "unknown"),
            new Claim("role", "Agent")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal, repo);

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Fact]
    public async Task ValidateToken_MissingSubjectClaim_FailsAuthentication()
    {
        var repo = new FakeStaffAccountRepository();

        var claims = new[]
        {
            new Claim("role", "Agent")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal, repo);

        await StaffAccountTokenValidator.ValidateTokenAccountActiveAsync(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.Contains("Missing subject claim", context.Result!.Failure!.Message);
    }
}
