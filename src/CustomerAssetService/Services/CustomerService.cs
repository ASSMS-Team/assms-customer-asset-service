using CustomerAssetService.DTOs;
using CustomerAssetService.Extensions;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using MySqlConnector;

namespace CustomerAssetService.Services;

public class CustomerService
{
    private const int DuplicateEntryErrorNumber = 1062;

    private readonly ICustomerRepository _repository;

    public CustomerService(ICustomerRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<CustomerResponse>> CreateAsync(CreateCustomerRequest request)
    {
        var phoneNormalized = PhoneNormalizer.Normalize(request.Phone);

        if (await _repository.ActivePhoneExistsAsync(phoneNormalized))
        {
            return Result<CustomerResponse>.Failure(ServiceError.DuplicatePhone);
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Phone = request.Phone,
            PhoneNormalized = phoneNormalized,
            Address = request.Address,
            CustomerType = request.CustomerType,
            Email = request.Email,
            Status = "ACTIVE"
        };

        try
        {
            await _repository.CreateAsync(customer);
        }
        catch (MySqlException ex) when (ex.Number == DuplicateEntryErrorNumber)
        {
            // The unique index is what actually enforces this: two requests can
            // both clear the check above before either one inserts.
            return Result<CustomerResponse>.Failure(ServiceError.DuplicatePhone);
        }

        // created_at and updated_at are database defaults, so the row is read
        // back rather than returning default(DateTime) for both.
        var created = await _repository.GetByIdAsync(customer.Id) ?? customer;

        return Result<CustomerResponse>.Success(MapToResponse(created));
    }

    public async Task<CustomerResponse?> GetByIdAsync(string id)
    {
        var customer = await _repository.GetByIdAsync(id);

        return customer is null ? null : MapToResponse(customer);
    }

    private static CustomerResponse MapToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Address = customer.Address,
        CustomerType = customer.CustomerType,
        Email = customer.Email,
        Status = customer.Status,
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt
    };
}
