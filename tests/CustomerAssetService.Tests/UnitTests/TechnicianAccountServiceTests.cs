using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class TechnicianAccountServiceTests
{
    private const string TechId = "783a9fae-a2da-4acb-906a-74cbfa5fbc6e";
    private static StaffAccount Account(string role = "Technician", string? linkedId = null, string status = "ACTIVE") => new()
    {
        Id = "staff-id", Username = "different.username", Email = "tech@example.com",
        PasswordHash = "existing-password-hash", Role = role, Status = status, TechnicianId = linkedId
    };
    private static CreateTechnicianAccountRequest Request() => new() { Username = "tech.login", Email = "login@example.com", Password = "ExamplePassword!42" };

    [Fact]
    public async Task CreatesOnlyTechnicianRoleWithHashedPasswordAndExplicitId()
    {
        var repo = new FakeRepository(); var hasher = new PasswordHasher();
        var result = await new TechnicianAccountService(repo, hasher).CreateAsync(TechId, Request());
        Assert.NotNull(result); Assert.Equal(TechId, result.TechnicianId);
        Assert.Equal(StaffRoles.Technician, result.Role);
        Assert.NotEqual(Request().Password, result.PasswordHash);
        Assert.True(hasher.Verify(Request().Password, result.PasswordHash));
    }

    [Fact]
    public async Task DuplicateTechnicianDoesNotCreateOrResetAccount()
    {
        var repo = new FakeRepository { Account = Account(linkedId: TechId) };
        Assert.Null(await new TechnicianAccountService(repo, new PasswordHasher()).CreateAsync(TechId, Request()));
        Assert.Equal(0, repo.Creates); Assert.Equal("existing-password-hash", repo.Account.PasswordHash);
    }

    [Fact]
    public async Task ExistingIdentifierCannotBeOverwritten()
    {
        var repo = new FakeRepository { Account = Account() };
        Assert.Null(await new TechnicianAccountService(repo, new PasswordHasher()).CreateAsync(TechId, Request()));
        Assert.Equal(0, repo.Creates);
    }

    [Fact]
    public async Task LinkingPreservesStaffIdPasswordAndDoesNotRequireMatchingReference()
    {
        var repo = new FakeRepository { Account = Account() };
        var result = await new TechnicianAccountService(repo, new PasswordHasher()).LinkAsync(TechId, "different.username");
        Assert.Equal(TechId, result!.TechnicianId); Assert.Equal("staff-id", result.Id);
        Assert.Equal("existing-password-hash", result.PasswordHash); Assert.Equal(0, repo.Creates);
    }

    [Theory]
    [InlineData("Manager", "ACTIVE")]
    [InlineData("Dispatcher", "ACTIVE")]
    [InlineData("Agent", "ACTIVE")]
    [InlineData("Technician", "INACTIVE")]
    public async Task LinkingRejectsOtherRolesAndInactiveAccounts(string role, string status)
    {
        var repo = new FakeRepository { Account = Account(role, status: status) };
        Assert.Null(await new TechnicianAccountService(repo, new PasswordHasher()).LinkAsync(TechId, "existing"));
        Assert.Equal(0, repo.Links);
    }

    [Fact]
    public async Task ExistingLinkCannotBeReassigned()
    {
        var repo = new FakeRepository { Account = Account(linkedId: Guid.NewGuid().ToString()) };
        Assert.Null(await new TechnicianAccountService(repo, new PasswordHasher()).LinkAsync(TechId, "existing"));
        Assert.Equal(0, repo.Links);
    }

    [Fact]
    public async Task RepeatLinkIsIdempotent()
    {
        var repo = new FakeRepository { Account = Account(linkedId: TechId) };
        Assert.NotNull(await new TechnicianAccountService(repo, new PasswordHasher()).LinkAsync(TechId, "existing"));
        Assert.Equal(0, repo.Links);
    }

    internal sealed class FakeRepository : ITechnicianAccountRepository
    {
        public StaffAccount? Account; public int Creates; public int Links;
        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) => Task.FromResult(Account);
        public Task<StaffAccount?> FindByTechnicianIdAsync(string id) => Task.FromResult(Account?.TechnicianId == id ? Account : null);
        public Task<bool> AnyManagerAsync() => Task.FromResult(true);
        public Task CreateAsync(StaffAccount account) { Account = account; Creates++; return Task.CompletedTask; }
        public Task<bool> LinkTechnicianAsync(string accountId, string id)
        {
            Links++;
            Account = new StaffAccount { Id = Account!.Id, Username = Account.Username, Email = Account.Email, PasswordHash = Account.PasswordHash, Role = Account.Role, Status = Account.Status, TechnicianId = id };
            return Task.FromResult(true);
        }
    }
}
