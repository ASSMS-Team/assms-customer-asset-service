using CustomerAssetService.Controllers;
using CustomerAssetService.DTOs;
using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class TechnicianAccountsControllerTests
{
    [Fact]
    public void ProvisioningRequiresDispatcherOrManagerWithoutAnonymousAccess()
    {
        var attribute = Assert.Single(typeof(TechnicianAccountsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Dispatcher,Manager", attribute.Roles);
        Assert.Empty(typeof(TechnicianAccountsController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    public async Task DoesNotCreateAccountWhenDispatchVerificationFails(int status)
    {
        var repo = new TechnicianAccountServiceTests.FakeRepository();
        var controller = new TechnicianAccountsController(new TechnicianAccountService(repo, new PasswordHasher()), new Verifier(status))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsType<ObjectResult>(await controller.Create(Guid.NewGuid(), new CreateTechnicianAccountRequest { Username = "tech", Email = "t@example.com", Password = "ExamplePassword42" }, default));
        Assert.Equal(status, result.StatusCode); Assert.Equal(0, repo.Creates);
    }
    private sealed class Verifier(int status) : IDispatchTechnicianVerifier
    {
        public Task<int> VerifyAsync(string id, string authorization, CancellationToken cancellationToken) => Task.FromResult(status);
    }
}
