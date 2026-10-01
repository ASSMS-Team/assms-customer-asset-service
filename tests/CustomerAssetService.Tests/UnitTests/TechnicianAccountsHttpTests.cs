using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class TechnicianAccountsHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Agent", HttpStatusCode.Forbidden)]
    [InlineData("Technician", HttpStatusCode.Forbidden)]
    [InlineData("Dispatcher", HttpStatusCode.Created)]
    [InlineData("Manager", HttpStatusCode.Created)]
    public async Task ActualMiddlewareRestrictsAccountCreation(string? role, HttpStatusCode expected)
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(role));
        var response = await client.PostAsJsonAsync($"/api/auth/technician-accounts/{Guid.NewGuid()}",
            new CreateTechnicianAccountRequest { Username = "new.login", Email = "new@example.com", Password = "ExamplePassword42!" });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Technician", Assert.Single(factory.Repo.Accounts).Role);
        }
        else Assert.Empty(factory.Repo.Accounts);
    }

    [Fact]
    public async Task ProvisionThenLoginIssuesTechnicianClaimAndRejectsDuplicate()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("Dispatcher"));
        var id = Guid.NewGuid();
        var request = new CreateTechnicianAccountRequest { Username = "separate.login", Email = "separate@example.com", Password = "ExamplePassword42!" };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/auth/technician-accounts/{id}", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/auth/technician-accounts/{id}", request)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = "separate.login", Password = request.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var result = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(id.ToString(), result.Staff.TechnicianId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        var me = await client.GetStringAsync("/api/auth/me");
        Assert.Contains(id.ToString(), me);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/auth/technician-accounts/{Guid.NewGuid()}", request)).StatusCode);
    }

    [Fact]
    public async Task LinksExistingAccountWithoutChangingItsPasswordOrStaffId()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        var account = new StaffAccount { Id = Guid.NewGuid().ToString(), Username = "existing.login", Email = "existing@example.com", PasswordHash = new PasswordHasher().Hash("ExistingPassword42!"), Role = "Technician", Status = "ACTIVE" };
        factory.Repo.Accounts.Add(account);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token("Manager"));
        var id = Guid.NewGuid();
        var response = await client.PutAsJsonAsync($"/api/auth/technician-accounts/{id}/link", new LinkTechnicianAccountRequest { Identifier = account.Username });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var linked = Assert.Single(factory.Repo.Accounts);
        Assert.Equal(account.Id, linked.Id); Assert.Equal(account.PasswordHash, linked.PasswordHash);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/auth/technician-accounts/{Guid.NewGuid()}/link", new LinkTechnicianAccountRequest { Identifier = account.Username })).StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public Repository Repo { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            foreach (var setting in new Dictionary<string, string?>
            {
                ["Authentication:Jwt:SigningKey"] = "test-only-signing-key-that-is-at-least-32-bytes",
                ["Authentication:Jwt:Issuer"] = "test-issuer", ["Authentication:Jwt:Audience"] = "test-audience",
                ["InternalServiceAuthentication:Key"] = "test-only-internal-key-at-least-32-bytes",
                ["ConnectionStrings:default"] = "Server=unused;Database=unused"
            }) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStaffAccountRepository>(); services.RemoveAll<ITechnicianAccountRepository>();
                services.RemoveAll<IDispatchTechnicianVerifier>();
                services.AddSingleton<IStaffAccountRepository>(Repo); services.AddSingleton<ITechnicianAccountRepository>(Repo);
                services.AddSingleton<IDispatchTechnicianVerifier>(new Verifier());
            });
        }
        public string Token(string role) => Services.GetRequiredService<ITokenService>().Issue(new StaffAccount { Id = Guid.NewGuid().ToString(), Username = "operator", Email = "operator@example.com", PasswordHash = "unused", Role = role, Status = "ACTIVE" }).AccessToken;
    }
    private sealed class Verifier : IDispatchTechnicianVerifier
    {
        public Task<int> VerifyAsync(string id, string auth, CancellationToken cancellationToken) => Task.FromResult(200);
    }
    private sealed class Repository : ITechnicianAccountRepository
    {
        public List<StaffAccount> Accounts { get; } = [];
        public Task<bool> AnyManagerAsync() => Task.FromResult(true);
        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) => Task.FromResult(Accounts.FirstOrDefault(a => a.Username.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase) || a.Email.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase)));
        public Task<StaffAccount?> FindByTechnicianIdAsync(string id) => Task.FromResult(Accounts.FirstOrDefault(a => a.TechnicianId == id));
        public Task CreateAsync(StaffAccount account) { Accounts.Add(account); return Task.CompletedTask; }
        public Task<bool> LinkTechnicianAsync(string accountId, string id)
        {
            var index = Accounts.FindIndex(a => a.Id == accountId); var a = Accounts[index];
            Accounts[index] = new StaffAccount { Id = a.Id, Username = a.Username, Email = a.Email, PasswordHash = a.PasswordHash, Role = a.Role, Status = a.Status, TechnicianId = id };
            return Task.FromResult(true);
        }
    }
}



