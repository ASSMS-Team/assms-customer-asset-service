using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Security;
using CustomerAssetService.Services;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class StaffAccountServiceTests
{
    private const string ManagerId = "manager-001";

    private static CreateStaffAccountRequest AgentRequest() => new()
    {
        FullName = "Alice Agent", Username = "alice.agent", Email = "alice@example.com",
        Role = "Agent", Password = "SecurePassword12"
    };

    private static CreateStaffAccountRequest TechRequest(string techId) => new()
    {
        FullName = "Bob Technician", Username = "bob.tech", Email = "bob@example.com",
        Role = "Technician", Password = "SecurePassword12", TechnicianId = techId
    };

    [Theory]
    [InlineData("Agent")]
    [InlineData("Dispatcher")]
    [InlineData("Manager")]
    public async Task CreatesNonTechnicianAccountsWithHashedPassword(string role)
    {
        var repo = new FakeRepo(); var hasher = new PasswordHasher();
        var service = new StaffAccountService(repo, repo, hasher, new FakeVerifier(200));
        var request = AgentRequest(); request.Role = role;
        var result = await service.CreateAsync(request, ManagerId, "Bearer tok", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(role, result.Value!.Role);
        Assert.Equal("ACTIVE", result.Value.Status);
        Assert.Equal(ManagerId, result.Value.CreatedBy);
        Assert.True(hasher.Verify(request.Password, result.Value.PasswordHash));
        Assert.Null(result.Value.TechnicianId);
    }

    [Fact]
    public async Task CreatesTechnicianWithLinkedDispatchId()
    {
        var techId = Guid.NewGuid().ToString();
        var repo = new FakeRepo(); var hasher = new PasswordHasher();
        var service = new StaffAccountService(repo, repo, hasher, new FakeVerifier(200));
        var result = await service.CreateAsync(TechRequest(techId), ManagerId, "Bearer tok", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Technician", result.Value!.Role);
        Assert.Equal(techId, result.Value.TechnicianId);
    }

    [Fact]
    public async Task TechnicianCreationFailsWithoutTechnicianId()
    {
        var service = new StaffAccountService(new FakeRepo(), new FakeRepo(), new PasswordHasher(), new FakeVerifier(200));
        var request = new CreateStaffAccountRequest
        {
            FullName = "Tech", Username = "tech", Email = "t@example.com",
            Role = "Technician", Password = "SecurePassword12"
        };
        var result = await service.CreateAsync(request, ManagerId, "Bearer tok", CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.TechnicianRequired, result.Error);
    }

    [Fact]
    public async Task TechnicianCreationFailsWhenTechNotFound()
    {
        var service = new StaffAccountService(new FakeRepo(), new FakeRepo(), new PasswordHasher(), new FakeVerifier(404));
        var result = await service.CreateAsync(TechRequest(Guid.NewGuid().ToString()), ManagerId, "Bearer tok", CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.TechnicianNotFound, result.Error);
    }

    [Fact]
    public async Task DuplicateUsernameIsRejected()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount
        {
            Id = "existing", FullName = "Existing", Username = "alice.agent", Email = "other@example.com",
            PasswordHash = "hash", Role = "Agent", Status = "ACTIVE"
        });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var result = await service.CreateAsync(AgentRequest(), ManagerId, "Bearer tok", CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicateIdentifier, result.Error);
    }

    [Fact]
    public async Task UnsupportedRoleIsRejected()
    {
        var service = new StaffAccountService(new FakeRepo(), new FakeRepo(), new PasswordHasher(), new FakeVerifier(200));
        var request = AgentRequest(); request.Role = "SuperAdmin";
        var result = await service.CreateAsync(request, ManagerId, "Bearer tok", CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.ValidationFailed, result.Error);
    }

    [Fact]
    public async Task PasswordHashIsNeverThePlaintextPassword()
    {
        var service = new StaffAccountService(new FakeRepo(), new FakeRepo(), new PasswordHasher(), new FakeVerifier(200));
        var result = await service.CreateAsync(AgentRequest(), ManagerId, "Bearer tok", CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.NotEqual(AgentRequest().Password, result.Value!.PasswordHash);
    }

    [Fact]
    public async Task Controller_GetAll_ReturnsOkWithResponseList()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount
        {
            Id = "acc-1", FullName = "Sunil Dias", Username = "sunil.d", Email = "sunil@example.com",
            PasswordHash = "hash", Role = "Agent", Status = "ACTIVE", CreatedAt = DateTime.UtcNow
        });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service);

        var actionResult = await controller.GetAll();
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
        var list = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<StaffAccountResponse>>(ok.Value);
        var item = Assert.Single(list);
        Assert.Equal("Sunil Dias", item.FullName);
        Assert.Equal("sunil.d", item.Username);
    }

    [Fact]
    public async Task Controller_Create_ReturnsCreatedAtAction()
    {
        var repo = new FakeRepo();
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var request = AgentRequest();
        var actionResult = await controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<Microsoft.AspNetCore.Mvc.CreatedAtActionResult>(actionResult);
        var response = Assert.IsType<StaffAccountResponse>(created.Value);
        Assert.Equal(request.Username, response.Username);
        Assert.Equal("ACTIVE", response.Status);
    }

    [Fact]
    public async Task Controller_Create_Duplicate_ReturnsConflictProblem()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount
        {
            Id = "existing", FullName = "Existing", Username = "alice.agent", Email = "alice@example.com",
            PasswordHash = "hash", Role = "Agent", Status = "ACTIVE"
        });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var actionResult = await controller.Create(AgentRequest(), CancellationToken.None);
        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(actionResult);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task Controller_Create_TechnicianWithoutId_ReturnsBadRequestProblem()
    {
        var repo = new FakeRepo();
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var request = new CreateStaffAccountRequest
        {
            FullName = "Tech", Username = "tech", Email = "t@example.com",
            Role = "Technician", Password = "SecurePassword12", TechnicianId = null
        };
        var actionResult = await controller.Create(request, CancellationToken.None);
        var badRequest = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(actionResult);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(badRequest.Value);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task Controller_Create_DispatchDown_Returns503ServiceUnavailable()
    {
        var repo = new FakeRepo();
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(500));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer token";
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext }
        };

        var actionResult = await controller.Create(TechRequest(Guid.NewGuid().ToString()), CancellationToken.None);
        var objectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(actionResult);
        Assert.Equal(503, objectResult.StatusCode);
    }

    private sealed class FakeRepo : ITechnicianAccountRepository
    {
        public List<StaffAccount> Accounts { get; } = [];
        public Task<StaffAccount?> FindByIdentifierAsync(string identifier) =>
            Task.FromResult(Accounts.FirstOrDefault(a =>
                a.Username.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase) ||
                a.Email.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase)));
        public Task<StaffAccount?> FindByIdAsync(string id) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));
        public Task<bool> AnyManagerAsync() => Task.FromResult(true);
        public Task CreateAsync(StaffAccount account) { Accounts.Add(account); return Task.CompletedTask; }
        public Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync() =>
            Task.FromResult<IReadOnlyList<StaffAccount>>(Accounts.Where(a => a.Status == "ACTIVE").ToList());
        public Task<StaffAccount?> FindByTechnicianIdAsync(string id) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.TechnicianId == id));
        public Task<bool> LinkTechnicianAsync(string accountId, string id) => Task.FromResult(true);
    }

    private sealed class FakeVerifier(int status) : IDispatchTechnicianVerifier
    {
        public Task<int> VerifyAsync(string id, string authorization, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }
}
