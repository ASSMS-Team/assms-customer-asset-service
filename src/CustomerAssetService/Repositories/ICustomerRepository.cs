using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public interface ICustomerRepository
{
    Task CreateAsync(Customer customer);

    Task<Customer?> GetByIdAsync(string id);

    Task<List<Customer>> GetAllAsync();

    Task<bool> ActivePhoneExistsAsync(string phoneNormalized);
}
