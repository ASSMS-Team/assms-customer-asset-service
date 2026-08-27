using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface IAssetRepository
{
    Task CreateAsync(Asset asset);

    Task<Asset?> GetByIdAsync(string id);

    // Serials are unique across the whole table regardless of owner or status,
    // so unlike ActivePhoneExistsAsync there is nothing to scope or exclude.
    Task<bool> SerialExistsAsync(string serialNormalized);
}
