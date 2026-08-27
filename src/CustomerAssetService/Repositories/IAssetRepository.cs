using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface IAssetRepository
{
    Task CreateAsync(Asset asset);

    Task<Asset?> GetByIdAsync(string id);

    // Every asset the customer owns, whatever the customer's own status - a
    // deactivated customer's equipment history is still worth reading.
    Task<List<Asset>> GetByCustomerIdAsync(string customerId);

    // Serials are unique across the whole table regardless of owner or status,
    // so unlike ActivePhoneExistsAsync there is nothing to scope or exclude.
    Task<bool> SerialExistsAsync(string serialNormalized);
}
