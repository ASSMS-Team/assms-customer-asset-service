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

    [Fact]
    public async Task Controller_GetAll_WithStatusFilter_FiltersCorrectly()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "1", FullName = "Active", Username = "u1", Email = "e1@a.com", PasswordHash = "h", Role = "Agent", Status = "ACTIVE" });
        repo.Accounts.Add(new StaffAccount { Id = "2", FullName = "Inactive", Username = "u2", Email = "e2@a.com", PasswordHash = "h", Role = "Dispatcher", Status = "INACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service);

        var all = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.GetAll(null));
        var allList = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<StaffAccountResponse>>(all.Value);
        Assert.Equal(2, allList.Count());

        var activeOnly = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.GetAll("ACTIVE"));
        var activeList = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<StaffAccountResponse>>(activeOnly.Value);
        Assert.Single(activeList);
        Assert.Equal("ACTIVE", activeList.First().Status);

        var inactiveOnly = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.GetAll("INACTIVE"));
        var inactiveList = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<StaffAccountResponse>>(inactiveOnly.Value);
        Assert.Single(inactiveList);
        Assert.Equal("INACTIVE", inactiveList.First().Status);
    }

    [Fact]
    public async Task Controller_GetById_Existing_ReturnsOkWithResponse()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "acc-99", FullName = "Test Person", Username = "person", Email = "p@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service);

        var actionResult = await controller.GetById("acc-99");
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
        var response = Assert.IsType<StaffAccountResponse>(ok.Value);
        Assert.Equal("acc-99", response.Id);
        Assert.Equal("person", response.Username);
    }

    [Fact]
    public async Task Controller_GetById_NonExistent_ReturnsNotFoundProblem()
    {
        var repo = new FakeRepo();
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service);

        var actionResult = await controller.GetById("unknown-id");
        var notFound = Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundObjectResult>(actionResult);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(notFound.Value);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task DeactivatesActiveAccountSuccessfully()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "acc-target", FullName = "Agent Target", Username = "target.a", Email = "t@a.com", PasswordHash = "h", Role = "Agent", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));

        var result = await service.DeactivateAsync("acc-target", "current-manager-id");
        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", result.Value!.Status);
        Assert.Equal("INACTIVE", repo.Accounts.First(a => a.Id == "acc-target").Status);
    }

    [Fact]
    public async Task Deactivate_SelfDeactivation_IsRejected()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "current-manager", FullName = "Me", Username = "me", Email = "me@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));

        var result = await service.DeactivateAsync("current-manager", "current-manager");
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.SelfDeactivationNotAllowed, result.Error);
    }

    [Fact]
    public async Task Deactivate_LastActiveManager_IsRejected()
    {
        var repo = new FakeRepo();
        // Only 1 manager in repo
        repo.Accounts.Add(new StaffAccount { Id = "other-manager", FullName = "Sole Manager", Username = "mgr", Email = "mgr@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));

        var result = await service.DeactivateAsync("other-manager", "different-caller-id");
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.LastActiveManager, result.Error);
    }

    [Fact]
    public async Task Deactivate_ManagerAllowedWhenOtherManagersExist()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "mgr-1", FullName = "Manager 1", Username = "mgr1", Email = "mgr1@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        repo.Accounts.Add(new StaffAccount { Id = "mgr-2", FullName = "Manager 2", Username = "mgr2", Email = "mgr2@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));

        var result = await service.DeactivateAsync("mgr-2", "mgr-1");
        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", result.Value!.Status);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_IsIdempotent()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "acc-already-off", FullName = "Off", Username = "off", Email = "off@a.com", PasswordHash = "h", Role = "Agent", Status = "INACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));

        var result = await service.DeactivateAsync("acc-already-off", "caller");
        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", result.Value!.Status);
    }

    [Fact]
    public async Task DeactivatedAccountCannotLogIn()
    {
        var repo = new FakeRepo();
        var hasher = new PasswordHasher();
        var rawPassword = "ValidPassword123!";
        var account = new StaffAccount { Id = "acc-target", FullName = "Agent Target", Username = "target.login", Email = "t@login.com", PasswordHash = hasher.Hash(rawPassword), Role = "Agent", Status = "ACTIVE" };
        repo.Accounts.Add(account);

        var staffService = new StaffAccountService(repo, repo, hasher, new FakeVerifier(200));
        var authService = new AuthenticationService(repo, hasher, new FakeTokenService());

        // Login succeeds while active
        var before = await authService.LoginAsync(new DTOs.LoginRequest { Identifier = "target.login", Password = rawPassword });
        Assert.NotNull(before);

        // Deactivate account
        var deactResult = await staffService.DeactivateAsync("acc-target", "manager-caller");
        Assert.True(deactResult.IsSuccess);

        // Login fails immediately after deactivation
        var after = await authService.LoginAsync(new DTOs.LoginRequest { Identifier = "target.login", Password = rawPassword });
        Assert.Null(after);
    }

    [Fact]
    public async Task Controller_Deactivate_ReturnsOkWithDeactivatedResponse()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "acc-1", FullName = "Agent 1", Username = "agent1", Email = "a1@a.com", PasswordHash = "h", Role = "Agent", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };

        var actionResult = await controller.Deactivate("acc-1");
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(actionResult);
        var response = Assert.IsType<StaffAccountResponse>(ok.Value);
        Assert.Equal("INACTIVE", response.Status);
    }

    [Fact]
    public async Task Controller_Deactivate_SelfDeactivation_ReturnsBadRequestProblem()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "current-mgr-sub", FullName = "Me", Username = "me", Email = "me@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, "current-mgr-sub")
        ]));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext }
        };

        var actionResult = await controller.Deactivate("current-mgr-sub");
        var badRequest = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(actionResult);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(badRequest.Value);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task Controller_Deactivate_LastManager_ReturnsConflictProblem()
    {
        var repo = new FakeRepo();
        repo.Accounts.Add(new StaffAccount { Id = "sole-mgr", FullName = "Sole", Username = "sole", Email = "s@a.com", PasswordHash = "h", Role = "Manager", Status = "ACTIVE" });
        var service = new StaffAccountService(repo, repo, new PasswordHasher(), new FakeVerifier(200));
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, "caller-id")
        ]));
        var controller = new CustomerAssetService.Controllers.StaffAccountsController(service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext }
        };

        var actionResult = await controller.Deactivate("sole-mgr");
        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(actionResult);
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
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
        public Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) =>
            Task.FromResult<IReadOnlyList<StaffAccount>>(string.IsNullOrWhiteSpace(status)
                ? Accounts.ToList()
                : Accounts.Where(a => a.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<StaffAccount?> FindByTechnicianIdAsync(string id) =>
            Task.FromResult(Accounts.FirstOrDefault(a => a.TechnicianId == id));
        public Task<bool> LinkTechnicianAsync(string accountId, string id) => Task.FromResult(true);
        public Task<bool> DeactivateAsync(string id)
        {
            var acc = Accounts.FirstOrDefault(a => a.Id == id);
            if (acc is null) return Task.FromResult(false);
            acc.Status = "INACTIVE";
            return Task.FromResult(true);
        }
        public Task<int> CountActiveManagersAsync() =>
            Task.FromResult(Accounts.Count(a => a.Role == "Manager" && a.Status == "ACTIVE"));
    }

    private sealed class FakeTokenService : ITokenService
    {
        public DTOs.LoginResponse Issue(StaffAccount account) =>
            new("fake-token", "Bearer", DateTime.UtcNow.AddHours(1),
                new DTOs.StaffIdentity(account.Id, account.Username, account.Email, account.Role, account.TechnicianId));
    }

    private sealed class FakeVerifier(int status) : IDispatchTechnicianVerifier
    {
        public Task<int> VerifyAsync(string id, string authorization, CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }
}
