using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class AuthenticationServiceTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public async Task Login_ValidActiveAccount_ReturnsToken()
    {
        var account = Account("ACTIVE");
        var tokenService = new RecordingTokenService();
        var service = new AuthenticationService(new FakeStaffRepository(account), _hasher, tokenService);

        var result = await service.LoginAsync(new LoginRequest { Identifier = "MANAGER@EXAMPLE.COM", Password = "Valid!234" });

        Assert.NotNull(result);
        Assert.Equal(account.Id, result.Staff.Id);
        Assert.Same(account, tokenService.IssuedFor);
    }

    [Theory]
    [InlineData("wrong", "ACTIVE")]
    [InlineData("Valid!234", "INACTIVE")]
    public async Task Login_InvalidPasswordOrInactiveAccount_ReturnsNoToken(string password, string status)
    {
        var tokenService = new RecordingTokenService();
        var service = new AuthenticationService(new FakeStaffRepository(Account(status)), _hasher, tokenService);

        var result = await service.LoginAsync(new LoginRequest { Identifier = "manager", Password = password });

        Assert.Null(result);
        Assert.Null(tokenService.IssuedFor);
    }

    [Fact]
    public async Task Login_UnknownAccount_ReturnsNoToken()
    {
        var service = new AuthenticationService(new FakeStaffRepository(null), _hasher, new RecordingTokenService());
        Assert.Null(await service.LoginAsync(new LoginRequest { Identifier = "missing", Password = "anything" }));
    }

    private StaffAccount Account(string status) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Username = "manager",
        Email = "manager@example.com",
        PasswordHash = _hasher.Hash("Valid!234"),
        Role = StaffRoles.Manager,
        Status = status
    };

    private sealed class FakeStaffRepository(StaffAccount? account) : IStaffAccountRepository
    {
        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) => Task.FromResult(account);
        public Task<bool> AnyManagerAsync() => Task.FromResult(account?.Role == StaffRoles.Manager);
        public Task CreateAsync(StaffAccount value) => Task.CompletedTask;
    }

    private sealed class RecordingTokenService : ITokenService
    {
        public StaffAccount? IssuedFor { get; private set; }

        public LoginResponse Issue(StaffAccount account)
        {
            IssuedFor = account;
            return new LoginResponse("token", "Bearer", DateTime.UtcNow.AddHours(1),
                new StaffIdentity(account.Id, account.Username, account.Email, account.Role));
        }
    }
}
