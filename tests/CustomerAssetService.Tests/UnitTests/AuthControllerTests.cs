using CustomerAssetService.Controllers;
using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Login_InvalidCredentials_ReturnsGenericUnauthorizedProblem()
    {
        var controller = new AuthController(Service(null));

        var action = await controller.Login(new LoginRequest { Identifier = "unknown", Password = "wrong" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(action);
        var problem = Assert.IsType<ProblemDetails>(unauthorized.Value);
        Assert.Equal("Invalid username/email or password.", problem.Detail);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsPublicIdentityWithoutHash()
    {
        var hasher = new PasswordHasher();
        var account = new StaffAccount
        {
            Id = "staff-1", Username = "manager", Email = "manager@example.com",
            PasswordHash = hasher.Hash("Valid!234"), Role = StaffRoles.Manager, Status = "ACTIVE"
        };
        var controller = new AuthController(Service(account, hasher));

        var action = await controller.Login(new LoginRequest { Identifier = account.Email, Password = "Valid!234" });

        var ok = Assert.IsType<OkObjectResult>(action);
        var response = Assert.IsType<LoginResponse>(ok.Value);
        Assert.Equal(account.Id, response.Staff.Id);
        Assert.DoesNotContain(account.PasswordHash, System.Text.Json.JsonSerializer.Serialize(response));
    }

    private static AuthenticationService Service(StaffAccount? account, PasswordHasher? hasher = null) =>
        new(new FakeRepository(account), hasher ?? new PasswordHasher(), new FakeTokenService());

    private sealed class FakeRepository(StaffAccount? account) : IStaffAccountRepository
    {
        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) => Task.FromResult(account);
        public Task<bool> AnyManagerAsync() => Task.FromResult(false);
        public Task CreateAsync(StaffAccount value) => Task.CompletedTask;
    }

    private sealed class FakeTokenService : ITokenService
    {
        public LoginResponse Issue(StaffAccount account) => new(
            "safe-token", "Bearer", DateTime.UtcNow.AddHours(1),
            new StaffIdentity(account.Id, account.Username, account.Email, account.Role));
    }
}
