using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using CustomerAssetService.Controllers;
using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Security;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace CustomerAssetService.Tests.UnitTests;

// Staff-account and technician-account behaviour not pinned by the existing
// tests: every controller error mapping, Dispatch synchronisation states and
// database duplicate handling.
public sealed class StaffAndTechnicianAccountMutationTests
{
    private const string ManagerId = "manager-1";
    private const string TechId = "6f1b6f0e-4f7d-4d9b-9a52-2b6d0f5e1a77";
    private static readonly PasswordHasher Hasher = new();

    private static MySqlException DbError(int number) =>
        (MySqlException)Activator.CreateInstance(typeof(MySqlException), BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: new object[] { (MySqlErrorCode)number, "database error" }, culture: null)!;

    private static StaffAccountService Service(FakeStaffAccountRepository repo, FakeDispatchVerifier? verifier = null) =>
        new(repo, repo, Hasher, verifier ?? new FakeDispatchVerifier());

    private static StaffAccountsController StaffController(StaffAccountService service, string? subject = ManagerId, string authorization = "Bearer manager-token")
    {
        var http = new DefaultHttpContext();
        if (subject is not null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)], "test"));
        http.Request.Headers.Authorization = authorization;
        return new StaffAccountsController(service) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static CreateStaffAccountRequest Request(string role = "Agent", string? technicianId = null) => new()
    {
        FullName = "  Alice Agent  ", Username = "  alice.agent  ", Email = "  alice@example.com  ",
        Role = role, Password = "SecurePassword12", TechnicianId = technicianId
    };

    private static ProblemDetails Problem(IActionResult result, int status)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode ?? (objectResult.Value as ProblemDetails)?.Status);
        var problem = Assert.IsAssignableFrom<ProblemDetails>(objectResult.Value);
        Assert.Equal(status, problem.Status);
        return problem;
    }

    // ---------- StaffAccountService.CreateAsync ----------

    [Fact]
    public async Task Create_TrimsFields_RecordsCreator_AndPersistsTheAccount()
    {
        var repo = new FakeStaffAccountRepository();

        var result = await Service(repo).CreateAsync(Request(), ManagerId, null, CancellationToken.None);

        var account = Assert.Single(repo.Created);
        Assert.Same(result.Value, account);
        Assert.Equal("Alice Agent", account.FullName);
        Assert.Equal("alice.agent", account.Username);
        Assert.Equal("alice@example.com", account.Email);
        Assert.Equal("ACTIVE", account.Status);
        Assert.Equal(ManagerId, account.CreatedBy);
        Assert.Null(account.TechnicianId);
        Assert.True(Hasher.Verify("SecurePassword12", account.PasswordHash));
    }

    [Fact]
    public async Task Create_RejectsDuplicateEmail_EvenWhenTheUsernameIsNew()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("other"));
        var request = Request();
        request.Email = "other@example.com";

        var result = await Service(repo).CreateAsync(request, ManagerId, null, CancellationToken.None);

        Assert.Equal(ServiceError.DuplicateIdentifier, result.Error);
        Assert.Empty(repo.Created);
    }

    [Fact]
    public async Task Create_MapsADuplicateKeyRaceToDuplicateIdentifier()
    {
        var repo = new FakeStaffAccountRepository { CreateException = MySqlExceptions.DuplicateKey("Duplicate entry 'alice.agent'") };

        var result = await Service(repo).CreateAsync(Request(), ManagerId, null, CancellationToken.None);

        Assert.Equal(ServiceError.DuplicateIdentifier, result.Error);
    }

    [Fact]
    public async Task Create_LetsOtherDatabaseErrorsPropagate()
    {
        var repo = new FakeStaffAccountRepository { CreateException = DbError(1213) };

        await Assert.ThrowsAsync<MySqlException>(() => Service(repo).CreateAsync(Request(), ManagerId, null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_Technician_VerifiesTheTrimmedIdWithDispatch_AndLinksIt()
    {
        var repo = new FakeStaffAccountRepository();
        var verifier = new FakeDispatchVerifier();

        var result = await Service(repo, verifier).CreateAsync(Request("Technician", $"  {TechId}  "), ManagerId, "Bearer t", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TechId, result.Value!.TechnicianId);
        Assert.Equal((TechId, "Bearer t"), Assert.Single(verifier.VerifyCalls));
    }

    [Fact]
    public async Task Create_Technician_SkipsDispatchVerification_WithoutAnAuthorizationHeader()
    {
        var verifier = new FakeDispatchVerifier { VerifyStatus = 503 };

        var result = await Service(new FakeStaffAccountRepository(), verifier).CreateAsync(Request("Technician", TechId), ManagerId, "", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(verifier.VerifyCalls);
    }

    [Theory]
    [InlineData(404, ServiceError.TechnicianNotFound)]
    [InlineData(503, ServiceError.DispatchUnavailable)]
    [InlineData(401, ServiceError.DispatchUnavailable)]
    public async Task Create_Technician_MapsDispatchVerificationFailures(int dispatchStatus, ServiceError expected)
    {
        var repo = new FakeStaffAccountRepository();

        var result = await Service(repo, new FakeDispatchVerifier { VerifyStatus = dispatchStatus })
            .CreateAsync(Request("Technician", TechId), ManagerId, "Bearer t", CancellationToken.None);

        Assert.Equal(expected, result.Error);
        Assert.Empty(repo.Created);
    }

    [Fact]
    public async Task Create_Technician_RejectsADispatchTechnicianThatIsAlreadyLinked()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("linked", "Technician", technicianId: TechId));

        var result = await Service(repo).CreateAsync(Request("Technician", TechId), ManagerId, null, CancellationToken.None);

        Assert.Equal(ServiceError.DuplicateIdentifier, result.Error);
    }

    // ---------- StaffAccountService.DeactivateAsync ----------

    [Fact]
    public async Task Deactivate_Technician_SendsTheTrimmedIdAndAuthorizationToDispatch_AndMarksSynchronized()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: $" {TechId} "));
        var verifier = new FakeDispatchVerifier();

        var result = await Service(repo, verifier).DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.Equal("INACTIVE", result.Value!.Status);
        Assert.Equal("SYNCHRONIZED", result.Value.DispatchSyncStatus);
        Assert.Equal((TechId, "Bearer m"), Assert.Single(verifier.DeactivateCalls));
        Assert.Equal(("tech", "SYNCHRONIZED"), Assert.Single(repo.Deactivations));
    }

    [Fact]
    public async Task Deactivate_Technician_PassesAnEmptyAuthorization_WhenNoneIsSupplied()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));
        var verifier = new FakeDispatchVerifier();

        await Service(repo, verifier).DeactivateAsync("tech", ManagerId, null, CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(verifier.DeactivateCalls).Authorization);
    }

    [Fact]
    public async Task Deactivate_Technician_TreatsAnUnknownDispatchTechnicianAsSynchronized()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));

        var result = await Service(repo, new FakeDispatchVerifier { DeactivationOutcome = DispatchDeactivationOutcome.NotFound })
            .DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.Equal("SYNCHRONIZED", result.Value!.DispatchSyncStatus);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveNonTechnician_ReturnsTheAccountWithoutAnyWrite()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("agent", status: "inactive"));
        var verifier = new FakeDispatchVerifier();

        var result = await Service(repo, verifier).DeactivateAsync("agent", ManagerId, "Bearer m", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(repo.Deactivations);
        Assert.Empty(repo.SyncUpdates);
        Assert.Empty(verifier.DeactivateCalls);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveSynchronizedTechnician_DoesNotCallDispatchAgain()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", "INACTIVE", TechId, "synchronized"));
        var verifier = new FakeDispatchVerifier();

        var result = await Service(repo, verifier).DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(verifier.DeactivateCalls);
        Assert.Empty(repo.SyncUpdates);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveTechnicianWithoutLink_DoesNotCallDispatch()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", "INACTIVE", technicianId: null, syncStatus: "PENDING_RETRY"));
        var verifier = new FakeDispatchVerifier();

        await Service(repo, verifier).DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.Empty(verifier.DeactivateCalls);
        Assert.Empty(repo.SyncUpdates);
    }

    [Fact]
    public async Task Deactivate_PendingTechnicianRetry_RecordsSynchronizedOnlyOnce()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", "INACTIVE", TechId, "PENDING_RETRY"));
        var service = Service(repo);

        var first = await service.DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.Equal("SYNCHRONIZED", first.Value!.DispatchSyncStatus);
        Assert.Equal(("tech", "SYNCHRONIZED"), Assert.Single(repo.SyncUpdates));
        Assert.Empty(repo.Deactivations);
    }

    [Fact]
    public async Task Deactivate_PendingTechnicianRetry_StillPending_DoesNotRewriteTheSameStatus()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", "INACTIVE", TechId, "PENDING_RETRY"));

        var result = await Service(repo, new FakeDispatchVerifier { DeactivationOutcome = DispatchDeactivationOutcome.Unavailable })
            .DeactivateAsync("tech", ManagerId, "Bearer m", CancellationToken.None);

        Assert.Equal("PENDING_RETRY", result.Value!.DispatchSyncStatus);
        Assert.Empty(repo.SyncUpdates);
    }

    [Fact]
    public async Task Deactivate_Manager_IsAllowedWhenTwoActiveManagersRemain()
    {
        var repo = new FakeStaffAccountRepository { ActiveManagerCount = 2 };
        repo.Accounts.Add(FakeStaffAccountRepository.Account("mgr2", "Manager"));

        var result = await Service(repo).DeactivateAsync("mgr2", ManagerId, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("INACTIVE", result.Value!.Status);
    }

    [Fact]
    public async Task Deactivate_Manager_IsRejectedWhenOnlyOneActiveManagerRemains()
    {
        var repo = new FakeStaffAccountRepository { ActiveManagerCount = 1 };
        repo.Accounts.Add(FakeStaffAccountRepository.Account("mgr2", "Manager"));

        Assert.Equal(ServiceError.LastActiveManager, (await Service(repo).DeactivateAsync("mgr2", ManagerId, null, CancellationToken.None)).Error);
        Assert.Empty(repo.Deactivations);
    }

    [Theory]
    [InlineData(CustomerAssetService.Repositories.DeactivateStaffAccountResult.NotFound, ServiceError.NotFound)]
    [InlineData(CustomerAssetService.Repositories.DeactivateStaffAccountResult.LastActiveManager, ServiceError.LastActiveManager)]
    public async Task Deactivate_MapsRepositoryRaceOutcomes(CustomerAssetService.Repositories.DeactivateStaffAccountResult repoResult, ServiceError expected)
    {
        var repo = new FakeStaffAccountRepository { DeactivateResult = repoResult };
        repo.Accounts.Add(FakeStaffAccountRepository.Account("agent"));

        Assert.Equal(expected, (await Service(repo).DeactivateAsync("agent", ManagerId, null, CancellationToken.None)).Error);
    }

    // ---------- StaffAccountsController ----------

    [Fact]
    public async Task Controller_GetById_ReturnsANotFoundProblemWithTitle()
    {
        var problem = Problem(await StaffController(Service(new FakeStaffAccountRepository())).GetById("missing"), 404);

        Assert.Equal("Staff account not found.", problem.Title);
    }

    [Fact]
    public async Task Controller_GetById_MapsEveryResponseField()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId, syncStatus: "SYNCHRONIZED"));

        var ok = Assert.IsType<OkObjectResult>(await StaffController(Service(repo)).GetById("tech"));

        var body = Assert.IsType<StaffAccountResponse>(ok.Value);
        Assert.Equal(new StaffAccountResponse("tech", "Full tech", "user.tech", "tech@example.com", "Technician", "ACTIVE", TechId,
            new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), "creator", "SYNCHRONIZED"), body);
    }

    [Fact]
    public async Task Controller_Create_RecordsTheAuthenticatedManagerAsCreator()
    {
        var repo = new FakeStaffAccountRepository();

        var created = Assert.IsType<CreatedAtActionResult>(await StaffController(Service(repo)).Create(Request(), CancellationToken.None));

        Assert.Equal(nameof(StaffAccountsController.GetById), created.ActionName);
        Assert.Equal(ManagerId, Assert.Single(repo.Created).CreatedBy);
    }

    [Fact]
    public async Task Controller_Create_RecordsAnEmptyCreator_WhenTheTokenHasNoSubject()
    {
        var repo = new FakeStaffAccountRepository();

        await StaffController(Service(repo), subject: null).Create(Request(), CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(repo.Created).CreatedBy);
    }

    [Fact]
    public async Task Controller_Create_ReturnsDuplicateProblem()
    {
        var repo = new FakeStaffAccountRepository { CreateException = MySqlExceptions.DuplicateKey("dup") };

        var problem = Problem(await StaffController(Service(repo)).Create(Request(), CancellationToken.None), 409);

        Assert.Equal("Duplicate identifier.", problem.Title);
        Assert.Equal("The username, email or technician link is already in use.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Create_ReturnsTechnicianRequiredProblem()
    {
        var problem = Problem(await StaffController(Service(new FakeStaffAccountRepository())).Create(Request("Technician"), CancellationToken.None), 400);

        Assert.Equal("Technician link required.", problem.Title);
        Assert.Equal("A Dispatch technician UUID is required when creating a Technician account.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Create_ReturnsTechnicianNotFoundProblem()
    {
        var service = Service(new FakeStaffAccountRepository(), new FakeDispatchVerifier { VerifyStatus = 404 });

        var problem = Problem(await StaffController(service).Create(Request("Technician", TechId), CancellationToken.None), 400);

        Assert.Equal("Technician not found.", problem.Title);
        Assert.Equal("The specified Dispatch technician does not exist.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Create_ReturnsDispatchUnavailableProblem()
    {
        var service = Service(new FakeStaffAccountRepository(), new FakeDispatchVerifier { VerifyStatus = 503 });

        var problem = Problem(await StaffController(service).Create(Request("Technician", TechId), CancellationToken.None), 503);

        Assert.Equal("Dispatch service unavailable.", problem.Title);
        Assert.Equal("Could not verify the technician with the Dispatch service. Try again later.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Create_ReturnsValidationProblem_ForUnsupportedRole()
    {
        var problem = Problem(await StaffController(Service(new FakeStaffAccountRepository())).Create(Request("Owner"), CancellationToken.None), 400);

        Assert.Equal("Validation failed.", problem.Title);
        Assert.Equal("Check that the role is Agent, Dispatcher, Technician or Manager.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Deactivate_ReturnsNotFoundProblem()
    {
        var problem = Problem(await StaffController(Service(new FakeStaffAccountRepository())).Deactivate("missing"), 404);

        Assert.Equal("Staff account not found.", problem.Title);
        Assert.Equal("The specified staff account does not exist.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Deactivate_ReturnsSelfDeactivationProblem()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account(ManagerId, "Manager"));

        var problem = Problem(await StaffController(Service(repo)).Deactivate(ManagerId), 400);

        Assert.Equal("Self-deactivation is not permitted.", problem.Title);
        Assert.Equal("You cannot deactivate your own currently authenticated Manager account.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Deactivate_UsesTheTokenSubject_SoAnotherManagerCanBeDeactivated()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("mgr2", "Manager"));

        Assert.IsType<OkObjectResult>(await StaffController(Service(repo), subject: ManagerId).Deactivate("mgr2"));
    }

    [Fact]
    public async Task Controller_Deactivate_ReturnsLastManagerProblem()
    {
        var repo = new FakeStaffAccountRepository { ActiveManagerCount = 1 };
        repo.Accounts.Add(FakeStaffAccountRepository.Account("mgr2", "Manager"));

        var problem = Problem(await StaffController(Service(repo)).Deactivate("mgr2"), 409);

        Assert.Equal("Last active Manager safeguard.", problem.Title);
        Assert.Equal("Cannot deactivate the last active Manager account. At least one active Manager must remain.", problem.Detail);
    }

    [Fact]
    public async Task Controller_Deactivate_ExposesPendingRetryHeader_WhenDispatchIsDown()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));
        var controller = StaffController(Service(repo, new FakeDispatchVerifier { DeactivationOutcome = DispatchDeactivationOutcome.Unavailable }));

        var ok = Assert.IsType<OkObjectResult>(await controller.Deactivate("tech"));

        Assert.Equal("PENDING_RETRY", Assert.IsType<StaffAccountResponse>(ok.Value).DispatchSyncStatus);
        Assert.Equal("PENDING_RETRY", controller.Response.Headers["X-Dispatch-Sync-Status"].ToString());
    }

    [Fact]
    public async Task Controller_Deactivate_DoesNotAddTheRetryHeader_WhenSynchronized()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));
        var controller = StaffController(Service(repo));

        Assert.IsType<OkObjectResult>(await controller.Deactivate("tech"));

        Assert.False(controller.Response.Headers.ContainsKey("X-Dispatch-Sync-Status"));
    }

    // ---------- TechnicianAccountService + TechnicianAccountsController ----------

    private static TechnicianAccountsController TechController(FakeStaffAccountRepository repo, FakeDispatchVerifier verifier)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer dispatcher";
        return new TechnicianAccountsController(new TechnicianAccountService(repo, Hasher), verifier)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static CreateTechnicianAccountRequest TechRequest() => new()
    {
        Username = " tech.one ", Email = " tech.one@example.com ", Password = "TechPassword12"
    };

    [Fact]
    public async Task TechController_Get_ReturnsTheLinkedLogin_OrNotFound()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));
        var controller = TechController(repo, new FakeDispatchVerifier());

        var ok = Assert.IsType<OkObjectResult>(await controller.Get(Guid.Parse(TechId)));
        Assert.Equal(new TechnicianAccountResponse("tech", "user.tech", "tech@example.com", TechId), ok.Value);
        Assert.IsType<NotFoundResult>(await controller.Get(Guid.NewGuid()));
    }

    [Fact]
    public async Task TechController_Create_CreatesATrimmedTechnicianLogin()
    {
        var repo = new FakeStaffAccountRepository();
        var verifier = new FakeDispatchVerifier();

        var created = Assert.IsType<CreatedAtActionResult>(await TechController(repo, verifier).Create(Guid.Parse(TechId), TechRequest(), CancellationToken.None));

        var account = Assert.Single(repo.Created);
        Assert.Equal("tech.one", account.Username);
        Assert.Equal("tech.one", account.FullName);
        Assert.Equal("tech.one@example.com", account.Email);
        Assert.Equal("Technician", account.Role);
        Assert.Equal("ACTIVE", account.Status);
        Assert.Equal(TechId, account.TechnicianId);
        Assert.True(Hasher.Verify("TechPassword12", account.PasswordHash));
        Assert.Equal((TechId, "Bearer dispatcher"), Assert.Single(verifier.VerifyCalls));
        Assert.Equal(nameof(TechnicianAccountsController.Get), created.ActionName);
    }

    [Theory]
    [InlineData(404, "Technician not found.")]
    [InlineData(503, "Unable to verify technician with Dispatch. Try again later.")]
    public async Task TechController_Create_ReportsDispatchVerificationFailures(int dispatchStatus, string title)
    {
        var repo = new FakeStaffAccountRepository();

        var problem = Problem(await TechController(repo, new FakeDispatchVerifier { VerifyStatus = dispatchStatus })
            .Create(Guid.Parse(TechId), TechRequest(), CancellationToken.None), dispatchStatus);

        Assert.Equal(title, problem.Title);
        Assert.Empty(repo.Created);
    }

    [Theory]
    [InlineData("linked-tech")]
    [InlineData("username")]
    [InlineData("email")]
    public async Task TechController_Create_ReturnsConflict_WhenTechnicianUsernameOrEmailAlreadyExists(string clash)
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(clash switch
        {
            "linked-tech" => FakeStaffAccountRepository.Account("x", "Technician", technicianId: TechId),
            "username" => new StaffAccount { Id = "y", FullName = "Y", Username = "tech.one", Email = "y@example.com", PasswordHash = "h", Role = "Agent", Status = "ACTIVE" },
            _ => new StaffAccount { Id = "z", FullName = "Z", Username = "z", Email = "tech.one@example.com", PasswordHash = "h", Role = "Agent", Status = "ACTIVE" },
        });

        var problem = Problem(await TechController(repo, new FakeDispatchVerifier()).Create(Guid.Parse(TechId), TechRequest(), CancellationToken.None), 409);

        Assert.Equal("Username, email or technician is already linked. Use existing-login setup if appropriate.", problem.Title);
        Assert.Empty(repo.Created);
    }

    [Fact]
    public async Task TechService_Create_ReturnsNullOnDuplicateKeyRace_AndRethrowsOtherErrors()
    {
        var service = new TechnicianAccountService(new FakeStaffAccountRepository { CreateException = MySqlExceptions.DuplicateKey("dup") }, Hasher);
        Assert.Null(await service.CreateAsync(TechId, TechRequest()));

        var failing = new TechnicianAccountService(new FakeStaffAccountRepository { CreateException = DbError(1213) }, Hasher);
        await Assert.ThrowsAsync<MySqlException>(() => failing.CreateAsync(TechId, TechRequest()));
    }

    [Fact]
    public async Task TechController_Link_LinksAnActiveUnlinkedTechnicianLogin()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician"));

        var ok = Assert.IsType<OkObjectResult>(await TechController(repo, new FakeDispatchVerifier())
            .Link(Guid.Parse(TechId), new LinkTechnicianAccountRequest { Identifier = "user.tech" }, CancellationToken.None));

        Assert.Equal(TechId, Assert.IsType<TechnicianAccountResponse>(ok.Value).TechnicianId);
    }

    [Theory]
    [InlineData(404, "Technician not found.")]
    [InlineData(500, "Unable to verify technician with Dispatch. Try again later.")]
    public async Task TechController_Link_ReportsDispatchVerificationFailures(int dispatchStatus, string title)
    {
        var problem = Problem(await TechController(new FakeStaffAccountRepository(), new FakeDispatchVerifier { VerifyStatus = dispatchStatus })
            .Link(Guid.Parse(TechId), new LinkTechnicianAccountRequest { Identifier = "user.tech" }, CancellationToken.None), dispatchStatus);

        Assert.Equal(title, problem.Title);
    }

    [Fact]
    public async Task TechController_Link_ReturnsConflict_WhenTheLoginCannotBeLinked()
    {
        var problem = Problem(await TechController(new FakeStaffAccountRepository(), new FakeDispatchVerifier())
            .Link(Guid.Parse(TechId), new LinkTechnicianAccountRequest { Identifier = "nobody" }, CancellationToken.None), 409);

        Assert.Equal("An active, unlinked Technician login is required. Existing identity links cannot be reassigned.", problem.Title);
    }

    [Fact]
    public async Task TechService_Link_ReturnsTheExistingLink_OnlyForTheSameAccount()
    {
        var repo = new FakeStaffAccountRepository();
        repo.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician", technicianId: TechId));
        repo.Accounts.Add(FakeStaffAccountRepository.Account("other", "Technician"));
        var service = new TechnicianAccountService(repo, Hasher);

        Assert.Equal("tech", (await service.LinkAsync(TechId, "user.tech"))!.Id);
        Assert.Null(await service.LinkAsync(TechId, "user.other"));
    }

    [Fact]
    public async Task TechService_Link_ReturnsNull_WhenTheRepositoryRefusesOrRacesTheLink()
    {
        var refused = new FakeStaffAccountRepository { LinkResult = false };
        refused.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician"));
        Assert.Null(await new TechnicianAccountService(refused, Hasher).LinkAsync(TechId, "user.tech"));

        var raced = new FakeStaffAccountRepository { LinkException = MySqlExceptions.DuplicateKey("dup") };
        raced.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician"));
        Assert.Null(await new TechnicianAccountService(raced, Hasher).LinkAsync(TechId, "user.tech"));

        var failing = new FakeStaffAccountRepository { LinkException = DbError(1213) };
        failing.Accounts.Add(FakeStaffAccountRepository.Account("tech", "Technician"));
        await Assert.ThrowsAsync<MySqlException>(() => new TechnicianAccountService(failing, Hasher).LinkAsync(TechId, "user.tech"));
    }
}
