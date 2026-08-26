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
}
