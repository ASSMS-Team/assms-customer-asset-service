using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public enum DeactivateStaffAccountResult
{
    Success,
    NotFound,
    LastActiveManager
}

public interface IStaffAccountRepository
{
    Task<StaffAccount?> FindByIdentifierAsync(string identifier);
    Task<StaffAccount?> FindByIdAsync(string id);
    Task<bool> AnyManagerAsync();
    Task CreateAsync(StaffAccount account);
    Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync();
    Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) => GetAllActiveAsync();
    Task<DeactivateStaffAccountResult> DeactivateAsync(string id) => Task.FromResult(DeactivateStaffAccountResult.NotFound);
    Task<DeactivateStaffAccountResult> DeactivateAsync(string id, string? dispatchSyncStatus) => DeactivateAsync(id);
    Task UpdateDispatchSyncStatusAsync(string id, string status) => Task.CompletedTask;
    Task<int> CountActiveManagersAsync() => Task.FromResult(1);
}

