using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface IStaffAccountRepository
{
    Task<StaffAccount?> FindByIdentifierAsync(string identifier);
    Task<StaffAccount?> FindByIdAsync(string id);
    Task<bool> AnyManagerAsync();
    Task CreateAsync(StaffAccount account);
    Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync();
    Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null) => GetAllActiveAsync();
    Task<bool> DeactivateAsync(string id) => Task.FromResult(false);
    Task<int> CountActiveManagersAsync() => Task.FromResult(1);
}

