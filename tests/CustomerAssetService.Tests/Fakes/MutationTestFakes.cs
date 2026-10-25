using System.Net;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CustomerAssetService.Tests;

// In-memory staff-account store shared by the seeder, staff-account and
// technician-account tests added for mutation testing.
public sealed class FakeStaffAccountRepository : ITechnicianAccountRepository
{
    public List<StaffAccount> Accounts { get; } = [];
    public List<StaffAccount> Created { get; } = [];
    public bool HasManager;
    public bool LinkResult = true;
    public Exception? CreateException;
    public Exception? LinkException;
    public List<(string Id, string Status)> SyncUpdates { get; } = [];
    public int ActiveManagerCount = 2;
    public DeactivateStaffAccountResult DeactivateResult = DeactivateStaffAccountResult.Success;
    public List<(string Id, string? SyncStatus)> Deactivations { get; } = [];

    public Task<StaffAccount?> FindByIdentifierAsync(string identifier) =>
        Task.FromResult(Accounts.FirstOrDefault(a =>
            a.Username.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase) ||
            a.Email.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<StaffAccount?> FindByIdAsync(string id) => Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));

    public Task<bool> AnyManagerAsync() => Task.FromResult(HasManager);

    public Task CreateAsync(StaffAccount account)
    {
        if (CreateException is not null) throw CreateException;
        Created.Add(account);
        Accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync() =>
        Task.FromResult<IReadOnlyList<StaffAccount>>(Accounts.Where(a => a.Status == "ACTIVE").ToList());

    public Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) =>
        Task.FromResult<IReadOnlyList<StaffAccount>>(status is null ? Accounts.ToList() : Accounts.Where(a => a.Status == status).ToList());

    public Task<DeactivateStaffAccountResult> DeactivateAsync(string id) => DeactivateAsync(id, null);

    public Task<DeactivateStaffAccountResult> DeactivateAsync(string id, string? dispatchSyncStatus)
    {
        Deactivations.Add((id, dispatchSyncStatus));
        var account = Accounts.FirstOrDefault(a => a.Id == id);
        if (DeactivateResult == DeactivateStaffAccountResult.Success && account is not null)
        {
            account.Status = "INACTIVE";
            account.DispatchSyncStatus = dispatchSyncStatus;
        }
        return Task.FromResult(DeactivateResult);
    }

    public Task UpdateDispatchSyncStatusAsync(string id, string status)
    {
        SyncUpdates.Add((id, status));
        var account = Accounts.FirstOrDefault(a => a.Id == id);
        if (account is not null) account.DispatchSyncStatus = status;
        return Task.CompletedTask;
    }

    public Task<int> CountActiveManagersAsync() => Task.FromResult(ActiveManagerCount);

    public Task<StaffAccount?> FindByTechnicianIdAsync(string technicianId) =>
        Task.FromResult(Accounts.FirstOrDefault(a => a.TechnicianId == technicianId));

    public Task<bool> LinkTechnicianAsync(string accountId, string technicianId)
    {
        if (LinkException is not null) throw LinkException;
        if (!LinkResult) return Task.FromResult(false);
        var index = Accounts.FindIndex(a => a.Id == accountId);
        var old = Accounts[index];
        Accounts[index] = new StaffAccount
        {
            Id = old.Id, FullName = old.FullName, Username = old.Username, Email = old.Email,
            PasswordHash = old.PasswordHash, Role = old.Role, Status = old.Status, TechnicianId = technicianId
        };
        return Task.FromResult(true);
    }

    public static StaffAccount Account(string id, string role = "Agent", string status = "ACTIVE",
        string? technicianId = null, string? syncStatus = null) => new()
    {
        Id = id, FullName = $"Full {id}", Username = $"user.{id}", Email = $"{id}@example.com",
        PasswordHash = "hash", Role = role, Status = status, TechnicianId = technicianId,
        DispatchSyncStatus = syncStatus, CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), CreatedBy = "creator"
    };
}

// Dispatch verifier stand-in that records what it was asked.
public sealed class FakeDispatchVerifier : IDispatchTechnicianVerifier
{
    public int VerifyStatus = 200;
    public DispatchDeactivationOutcome DeactivationOutcome = DispatchDeactivationOutcome.Success;
    public List<(string TechnicianId, string Authorization)> VerifyCalls { get; } = [];
    public List<(string TechnicianId, string Authorization)> DeactivateCalls { get; } = [];

    public Task<int> VerifyAsync(string technicianId, string authorization, CancellationToken cancellationToken)
    {
        VerifyCalls.Add((technicianId, authorization));
        return Task.FromResult(VerifyStatus);
    }

    public Task<DispatchDeactivationOutcome> DeactivateTechnicianAsync(string technicianId, string authorization, CancellationToken cancellationToken)
    {
        DeactivateCalls.Add((technicianId, authorization));
        return Task.FromResult(DeactivationOutcome);
    }
}

// Captures outgoing HTTP requests and answers with a configured response.
public sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Exception? Throw;

    public static RecordingHttpHandler Returning(HttpStatusCode status) => new(_ => new HttpResponseMessage(status));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Throw is not null) throw Throw;
        return Task.FromResult(respond(request));
    }
}

public sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "CustomerAssetService.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
