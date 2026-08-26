using CustomerAssetService.Models;
using CustomerAssetService.Repositories;

namespace CustomerAssetService.Tests;

// Hand-rolled stand-in for the real repository: every call records what it was
// given and hands back whatever the test configured, so a service test can run
// without a database.
public class FakeCustomerRepository : ICustomerRepository
{
    // CreateAsync
    public Customer? CreatedCustomer;
    public int CreateAsyncCallCount;
    // Left null for the happy path. Set it to make CreateAsync throw - a
    // MySqlException with number 1062 is what the service treats as the
    // duplicate-phone race the unique index catches.
    public Exception? ExceptionToThrow;

    // GetByIdAsync
    public string? GetByIdId;
    public Customer? CustomerToReturn;

    // GetAllAsync - defaults to an empty list, not null, so an unconfigured
    // fake stands in for "no customers yet" rather than blowing up the caller.
    public List<Customer> CustomersToReturn = new();

    // ActivePhoneExistsAsync
    public string? ActivePhoneExistsPhoneNormalized;
    public bool ActivePhoneExistsResult;

    public Task CreateAsync(Customer customer)
    {
        // Recorded and counted before the throw, so a test that configures an
        // exception can still assert on what the call was given.
        CreatedCustomer = customer;
        CreateAsyncCallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.CompletedTask;
    }

    public Task<Customer?> GetByIdAsync(string id)
    {
        GetByIdId = id;

        return Task.FromResult(CustomerToReturn);
    }

    public Task<List<Customer>> GetAllAsync()
    {
        return Task.FromResult(CustomersToReturn);
    }

    public Task<bool> ActivePhoneExistsAsync(string phoneNormalized)
    {
        ActivePhoneExistsPhoneNormalized = phoneNormalized;

        return Task.FromResult(ActivePhoneExistsResult);
    }
}
