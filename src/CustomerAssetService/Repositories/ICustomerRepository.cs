using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface ICustomerRepository
{
    Task CreateAsync(Customer customer);

    Task<Customer?> GetByIdAsync(string id);

    // status is optional so existing call sites keep working unchanged, same as
    // excludeCustomerId below; null means every customer whatever its status.
    Task<List<Customer>> GetAllAsync(string? status = null);

    Task UpdateAsync(Customer customer);

    Task DeactivateAsync(string id);

    // excludeCustomerId is optional so the create path can keep calling this with
    // one argument; the update path passes the customer being edited so its own
    // row does not count as a clash with itself.
    Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null);
}
