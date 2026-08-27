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

    // UpdateAsync
    public Customer? UpdatedCustomer;
    public int UpdateAsyncCallCount;

    // DeactivateAsync
    public string? DeactivatedId;
    public int DeactivateAsyncCallCount;

    // ActivePhoneExistsAsync
    public string? ActivePhoneExistsPhoneNormalized;
    // Null when the caller passed no id to exclude, which is how a test tells
    // the create path's call apart from the update path's.
    public string? ActivePhoneExistsExcludeCustomerId;
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

    public Task UpdateAsync(Customer customer)
    {
        // Recorded and counted before the throw, for the same reason as CreateAsync.
        UpdatedCustomer = customer;
        UpdateAsyncCallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.CompletedTask;
    }

    public Task DeactivateAsync(string id)
    {
        DeactivatedId = id;
        DeactivateAsyncCallCount++;

        // The real UPDATE changes the row, so the service's read-back sees the
        // new status; the fake would otherwise hand the same ACTIVE customer
        // straight back and hide that.
        if (CustomerToReturn is not null && CustomerToReturn.Id == id)
        {
            CustomerToReturn.Status = "INACTIVE";
        }

        return Task.CompletedTask;
    }

    public Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null)
    {
        ActivePhoneExistsPhoneNormalized = phoneNormalized;
        ActivePhoneExistsExcludeCustomerId = excludeCustomerId;

        // Stands in for the WHERE id != @excludeId: the excluded row is out of
        // the query, so a customer still holding its own number finds no clash
        // even when a clash was configured. Any other number still clashes.
        if (excludeCustomerId is not null
            && excludeCustomerId == CustomerToReturn?.Id
            && phoneNormalized == CustomerToReturn.PhoneNormalized)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(ActivePhoneExistsResult);
    }
}
