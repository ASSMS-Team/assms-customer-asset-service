using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface ICustomerRepository
{
    Task CreateAsync(Customer customer);

    Task<Customer?> GetByIdAsync(string id);

    Task<List<Customer>> GetAllAsync();

    Task UpdateAsync(Customer customer);

    Task DeactivateAsync(string id);

    // excludeCustomerId is optional so the create path can keep calling this with
    // one argument; the update path passes the customer being edited so its own
    // row does not count as a clash with itself.
    Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null);
}
