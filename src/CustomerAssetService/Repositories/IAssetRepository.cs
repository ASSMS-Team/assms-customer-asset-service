using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface IAssetRepository
{
    Task CreateAsync(Asset asset);

    Task<Asset?> GetByIdAsync(string id);

    // Every asset the customer owns, whatever the customer's own status - a
    // deactivated customer's equipment history is still worth reading.
    Task<List<Asset>> GetByCustomerIdAsync(string customerId);

    Task UpdateAsync(Asset asset);

    // Serials are unique across the whole table regardless of owner or status,
    // so unlike ActivePhoneExistsAsync there is nothing to scope. excludeAssetId
    // is optional so the create path can keep calling this with one argument;
    // the update path passes the asset being edited so its own row does not
    // count as a clash with itself.
    Task<bool> SerialExistsAsync(string serialNormalized, string? excludeAssetId = null);
}
