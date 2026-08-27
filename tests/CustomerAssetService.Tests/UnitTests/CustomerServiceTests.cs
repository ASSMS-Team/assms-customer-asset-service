using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Services;

namespace CustomerAssetService.Tests;

public class CustomerServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesActiveCustomer()
    {
        // Arrange - ActivePhoneExistsResult is left false, so the pre-check passes.
        var repository = new FakeCustomerRepository();
        var service = new CustomerService(repository);
        var request = new CreateCustomerRequest
        {
            Name = "Nimal Perera",
            Phone = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Email = "nimal@example.com"
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);

        var created = repository.CreatedCustomer;
        Assert.NotNull(created);
        // The id is server-generated, so the test asserts that one was assigned
        // rather than pinning a value it cannot know.
        Assert.NotEmpty(created!.Id);
        Assert.Equal("ACTIVE", created.Status);
        Assert.NotEmpty(created.Phone);
        Assert.NotEmpty(created.PhoneNormalized);
    }

    [Fact]
    public async Task CreateAsync_NormalizesPhone_WhileKeepingWhatWasTyped()
    {
        // Arrange
        var repository = new FakeCustomerRepository();
        var service = new CustomerService(repository);
        var request = new CreateCustomerRequest
        {
            Name = "Nimal Perera",
            Phone = "077-111-2222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert - this is what proves the service runs the normalizer at all;
        // the normalizer's own tests only prove the function works in isolation.
        Assert.True(result.IsSuccess);

        var created = repository.CreatedCustomer;
        Assert.NotNull(created);
        Assert.Equal("0771112222", created!.PhoneNormalized);
        Assert.Equal("077-111-2222", created.Phone);
    }

    [Fact]
    public async Task CreateAsync_WhenActivePhoneExists_FailsWithoutInserting()
    {
        // Arrange
        var repository = new FakeCustomerRepository
        {
            ActivePhoneExistsResult = true
        };
        var service = new CustomerService(repository);
        var request = new CreateCustomerRequest
        {
            Name = "Nimal Perera",
            Phone = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicatePhone, result.Error);
        // The valuable one: the pre-check short-circuits, so the insert is
        // never attempted rather than being attempted and rolled back.
        Assert.Equal(0, repository.CreateAsyncCallCount);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCustomerExists_ReturnsMappedResponse()
    {
        // Arrange
        var customer = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "077-111-2222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Email = "nimal@example.com",
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomerToReturn = customer
        };
        var service = new CustomerService(repository);

        // Act
        var response = await service.GetByIdAsync(customer.Id);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(customer.Id, response!.Id);
        Assert.Equal(customer.Name, response.Name);
        // Phone comes back as it was typed, not normalized.
        Assert.Equal(customer.Phone, response.Phone);
        Assert.Equal(customer.Address, response.Address);
        Assert.Equal(customer.CustomerType, response.CustomerType);
        Assert.Equal(customer.Email, response.Email);
        Assert.Equal(customer.Status, response.Status);
        Assert.Equal(customer.CreatedAt, response.CreatedAt);
        Assert.Equal(customer.UpdatedAt, response.UpdatedAt);
        Assert.Equal(customer.Id, repository.GetByIdId);
    }

    [Fact]
    public async Task GetAllAsync_WhenCustomersExist_ReturnsMappedResponses()
    {
        // Arrange
        var first = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "077-111-2222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Email = "nimal@example.com",
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc)
        };
        var second = new Customer
        {
            Id = "22222222-2222-2222-2222-222222222222",
            Name = "Kamal Silva",
            Phone = "0779998888",
            PhoneNormalized = "0779998888",
            Address = "48 Kandy Road, Kadawatha",
            CustomerType = "BUSINESS",
            Email = null,
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomersToReturn = new List<Customer> { first, second }
        };
        var service = new CustomerService(repository);

        // Act
        var responses = await service.GetAllAsync();

        // Assert - the service maps without reordering; the newest-first order
        // is the repository's ORDER BY, so it is preserved as handed over.
        Assert.Equal(2, responses.Count);

        Assert.Equal(first.Id, responses[0].Id);
        Assert.Equal(first.Name, responses[0].Name);
        Assert.Equal(first.Phone, responses[0].Phone);
        Assert.Equal(first.Address, responses[0].Address);
        Assert.Equal(first.CustomerType, responses[0].CustomerType);
        Assert.Equal(first.Email, responses[0].Email);
        Assert.Equal(first.Status, responses[0].Status);
        Assert.Equal(first.CreatedAt, responses[0].CreatedAt);
        Assert.Equal(first.UpdatedAt, responses[0].UpdatedAt);

        Assert.Equal(second.Id, responses[1].Id);
        Assert.Equal(second.Name, responses[1].Name);
        Assert.Null(responses[1].Email);
    }

    [Fact]
    public async Task GetAllAsync_WhenNoCustomers_ReturnsEmptyList()
    {
        // Arrange - CustomersToReturn is left at its empty default.
        var repository = new FakeCustomerRepository();
        var service = new CustomerService(repository);

        // Act
        var responses = await service.GetAllAsync();

        // Assert - an empty list, never null: the controller returns 200 with
        // an empty array rather than a 404 when nobody is registered.
        Assert.NotNull(responses);
        Assert.Empty(responses);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCustomerMissing_ReturnsNull()
    {
        // Arrange - CustomerToReturn is left null, standing in for no such row.
        var repository = new FakeCustomerRepository();
        var service = new CustomerService(repository);

        // Act
        var response = await service.GetByIdAsync("00000000-0000-0000-0000-000000000000");

        // Assert - a missing customer is an expected outcome, not an exception.
        Assert.Null(response);
    }
    [Fact]
    public async Task UpdateAsync_WithValidRequest_ChangesEditableFieldsOnly()
    {
        // Arrange - an ACTIVE customer to edit, and no clash on the new number.
        var existing = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "0771112222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Email = "nimal@example.com",
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomerToReturn = existing
        };
        var service = new CustomerService(repository);
        var request = new UpdateCustomerRequest
        {
            Name = "Nimal J. Perera",
            Phone = "077-999-8888",
            Address = "48 Kandy Road, Kadawatha",
            CustomerType = "BUSINESS",
            Email = "nimal.perera@example.com"
        };

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);
        Assert.Equal(1, repository.UpdateAsyncCallCount);

        var updated = repository.UpdatedCustomer;
        Assert.NotNull(updated);
        Assert.Equal(request.Name, updated!.Name);
        // Phone is stored as typed, alongside the normalized form.
        Assert.Equal("077-999-8888", updated.Phone);
        Assert.Equal("0779998888", updated.PhoneNormalized);
        Assert.Equal(request.Address, updated.Address);
        Assert.Equal(request.CustomerType, updated.CustomerType);
        Assert.Equal(request.Email, updated.Email);
        // The three the request may not touch are carried over unchanged.
        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal(existing.Status, updated.Status);
        Assert.Equal(existing.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WhenCustomerMissing_ReturnsNotFoundWithoutUpdating()
    {
        // Arrange - CustomerToReturn is left null, standing in for no such row.
        var repository = new FakeCustomerRepository();
        var service = new CustomerService(repository);
        var request = new UpdateCustomerRequest
        {
            Name = "Nimal Perera",
            Phone = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.UpdateAsync("00000000-0000-0000-0000-000000000000", request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Equal(0, repository.UpdateAsyncCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenCustomerNotActive_ReturnsCustomerInactiveWithoutUpdating()
    {
        // Arrange
        var existing = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "0771112222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Status = "INACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomerToReturn = existing
        };
        var service = new CustomerService(repository);
        var request = new UpdateCustomerRequest
        {
            Name = "Nimal J. Perera",
            Phone = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert - a deactivated customer is history; editing it is refused
        // before anything is written.
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.CustomerInactive, result.Error);
        Assert.Equal(0, repository.UpdateAsyncCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenAnotherActiveCustomerHasPhone_FailsWithoutUpdating()
    {
        // Arrange - the new number is held by somebody else, so the pre-check hits.
        var existing = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "0771112222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomerToReturn = existing,
            ActivePhoneExistsResult = true
        };
        var service = new CustomerService(repository);
        var request = new UpdateCustomerRequest
        {
            Name = "Nimal Perera",
            Phone = "0779998888",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicatePhone, result.Error);
        // The pre-check short-circuits, so the update is never attempted.
        Assert.Equal(0, repository.UpdateAsyncCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenPhoneUnchanged_ExcludesOwnRowAndSucceeds()
    {
        // Arrange - a clash is configured, so without the exclusion the customer
        // would be rejected for holding the number it already holds.
        var existing = new Customer
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Name = "Nimal Perera",
            Phone = "0771112222",
            PhoneNormalized = "0771112222",
            Address = "12 Galle Road, Colombo 03",
            CustomerType = "INDIVIDUAL",
            Status = "ACTIVE",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc)
        };
        var repository = new FakeCustomerRepository
        {
            CustomerToReturn = existing,
            ActivePhoneExistsResult = true
        };
        var service = new CustomerService(repository);
        var request = new UpdateCustomerRequest
        {
            // Only the address changes; the number is the same one, retyped.
            Name = "Nimal Perera",
            Phone = "077-111-2222",
            Address = "48 Kandy Road, Kadawatha",
            CustomerType = "INDIVIDUAL"
        };

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert - this is the one that matters: the customer's own id reaches
        // the repository as the row to exclude, so it does not clash with itself.
        Assert.Equal(existing.Id, repository.ActivePhoneExistsExcludeCustomerId);
        Assert.Equal("0771112222", repository.ActivePhoneExistsPhoneNormalized);
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);
        Assert.Equal(1, repository.UpdateAsyncCallCount);
        Assert.Equal("48 Kandy Road, Kadawatha", repository.UpdatedCustomer!.Address);
    }
}
