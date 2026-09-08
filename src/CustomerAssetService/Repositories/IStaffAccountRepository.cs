using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface IStaffAccountRepository
{
    Task<StaffAccount?> FindByIdentifierAsync(string identifier);
    Task<bool> AnyManagerAsync();
    Task CreateAsync(StaffAccount account);
}
