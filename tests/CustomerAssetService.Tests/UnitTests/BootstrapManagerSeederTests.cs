using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class BootstrapManagerSeederTests
{
    private static readonly PasswordHasher Hasher = new();

    private static BootstrapManagerSeeder Seeder(FakeStaffAccountRepository repo, string environment,
        Dictionary<string, string?> settings) =>
        new(repo, Hasher, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new FakeHostEnvironment(environment), NullLogger<BootstrapManagerSeeder>.Instance);

    private static Dictionary<string, string?> Bootstrap(string? username = " root.manager ", string? email = " root@example.com ", string? password = "BootstrapPass1") => new()
    {
        ["Authentication:BootstrapManager:Username"] = username,
        ["Authentication:BootstrapManager:Email"] = email,
        ["Authentication:BootstrapManager:Password"] = password,
    };

    private static Dictionary<string, string?> Seed(int index, string? username, string? email, string? password, string? role) => new()
    {
        [$"Authentication:SeedAccounts:{index}:Username"] = username,
        [$"Authentication:SeedAccounts:{index}:Email"] = email,
        [$"Authentication:SeedAccounts:{index}:Password"] = password,
        [$"Authentication:SeedAccounts:{index}:Role"] = role,
    };

    private static Dictionary<string, string?> Merge(params Dictionary<string, string?>[] parts) =>
        parts.SelectMany(p => p).ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task CreatesTrimmedActiveBootstrapManagerWithHashedPassword_WhenNoManagerExists()
    {
        var repo = new FakeStaffAccountRepository { HasManager = false };

        await Seeder(repo, "Production", Bootstrap()).EnsureCreatedAsync();

        var manager = Assert.Single(repo.Created);
        Assert.Equal("System Administrator", manager.FullName);
        Assert.Equal("root.manager", manager.Username);
        Assert.Equal("root@example.com", manager.Email);
        Assert.Equal(StaffRoles.Manager, manager.Role);
        Assert.Equal("ACTIVE", manager.Status);
        Assert.True(Guid.TryParse(manager.Id, out _));
        Assert.NotEqual("BootstrapPass1", manager.PasswordHash);
        Assert.True(Hasher.Verify("BootstrapPass1", manager.PasswordHash));
    }

    [Fact]
    public async Task DoesNotCreateBootstrapManager_WhenAManagerAlreadyExists()
    {
        var repo = new FakeStaffAccountRepository { HasManager = true };

        await Seeder(repo, "Production", Bootstrap()).EnsureCreatedAsync();

        Assert.Empty(repo.Created);
    }

    [Theory]
    [InlineData(null, "root@example.com", "BootstrapPass1")]
    [InlineData("  ", "root@example.com", "BootstrapPass1")]
    [InlineData("root.manager", null, "BootstrapPass1")]
    [InlineData("root.manager", " ", "BootstrapPass1")]
    [InlineData("root.manager", "root@example.com", null)]
    [InlineData("root.manager", "root@example.com", " ")]
    public async Task SkipsBootstrapManager_WhenAnyBootstrapSettingIsMissing(string? username, string? email, string? password)
    {
        var repo = new FakeStaffAccountRepository { HasManager = false };

        await Seeder(repo, "Production", Bootstrap(username, email, password)).EnsureCreatedAsync();

        Assert.Empty(repo.Created);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task CreatesApprovedSeedAccounts_InDevelopmentAndStaging(string environment)
    {
        var repo = new FakeStaffAccountRepository { HasManager = true };
        var settings = Merge(
            Seed(0, " agent.one ", " agent1@example.com ", "AgentPass123", " Agent "),
            Seed(1, "disp.one", "disp1@example.com", "DispPass123", "Dispatcher"),
            Seed(2, "tech.one", "tech1@example.com", "TechPass123", "Technician"),
            Seed(3, "mgr.two", "mgr2@example.com", "MgrPass1234", "Manager"));

        await Seeder(repo, environment, settings).EnsureCreatedAsync();

        Assert.Equal(4, repo.Created.Count);
        var agent = repo.Created[0];
        Assert.Equal("agent.one", agent.Username);
        Assert.Equal("agent.one", agent.FullName);
        Assert.Equal("agent1@example.com", agent.Email);
        Assert.Equal("Agent", agent.Role);
        Assert.Equal("ACTIVE", agent.Status);
        Assert.True(Guid.TryParse(agent.Id, out _));
        Assert.True(Hasher.Verify("AgentPass123", agent.PasswordHash));
        Assert.Equal(new[] { "Agent", "Dispatcher", "Technician", "Manager" }, repo.Created.Select(a => a.Role));
    }

    [Fact]
    public async Task DoesNotCreateSeedAccounts_OutsideDevelopmentAndStaging()
    {
        var repo = new FakeStaffAccountRepository { HasManager = true };

        await Seeder(repo, "Production", Seed(0, "agent.one", "agent1@example.com", "AgentPass123", "Agent")).EnsureCreatedAsync();

        Assert.Empty(repo.Created);
    }

    [Theory]
    [InlineData(null, "a@example.com", "Password123", "Agent")]
    [InlineData(" ", "a@example.com", "Password123", "Agent")]
    [InlineData("agent", null, "Password123", "Agent")]
    [InlineData("agent", " ", "Password123", "Agent")]
    [InlineData("agent", "a@example.com", null, "Agent")]
    [InlineData("agent", "a@example.com", " ", "Agent")]
    [InlineData("agent", "a@example.com", "Password123", null)]
    [InlineData("agent", "a@example.com", "Password123", "Administrator")]
    public async Task SkipsIncompleteOrUnsupportedSeedEntries_ButKeepsProcessingTheRest(string? username, string? email, string? password, string? role)
    {
        var repo = new FakeStaffAccountRepository { HasManager = true };
        var settings = Merge(Seed(0, username, email, password, role), Seed(1, "valid.user", "valid@example.com", "ValidPass123", "Agent"));

        await Seeder(repo, "Development", settings).EnsureCreatedAsync();

        var created = Assert.Single(repo.Created);
        Assert.Equal("valid.user", created.Username);
    }

    [Fact]
    public async Task SkipsSeedAccount_WhoseEmailAlreadyExists_AndContinuesWithTheNext()
    {
        var repo = new FakeStaffAccountRepository { HasManager = true };
        repo.Accounts.Add(FakeStaffAccountRepository.Account("existing"));
        var settings = Merge(
            Seed(0, "dup.user", "existing@example.com", "Password123", "Agent"),
            Seed(1, "new.user", "new@example.com", "Password123", "Agent"));

        await Seeder(repo, "Development", settings).EnsureCreatedAsync();

        var created = Assert.Single(repo.Created);
        Assert.Equal("new.user", created.Username);
    }

    [Fact]
    public async Task CreatesBootstrapManagerAndSeedAccounts_InTheSameDevelopmentRun()
    {
        var repo = new FakeStaffAccountRepository { HasManager = false };
        var settings = Merge(Bootstrap(), Seed(0, "agent.one", "agent1@example.com", "AgentPass123", "Agent"));

        await Seeder(repo, "Development", settings).EnsureCreatedAsync();

        Assert.Equal(new[] { "root.manager", "agent.one" }, repo.Created.Select(a => a.Username));
    }
}
