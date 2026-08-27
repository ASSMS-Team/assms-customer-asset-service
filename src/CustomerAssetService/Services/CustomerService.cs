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

    public async Task<Result<CustomerResponse>> UpdateAsync(string id, UpdateCustomerRequest request)
    {
        var existing = await _repository.GetByIdAsync(id);

        if (existing is null)
        {
            return Result<CustomerResponse>.Failure(ServiceError.NotFound);
        }

        // Only ACTIVE customers are editable - a deactivated record is history,
        // and rewriting it would quietly change what was already agreed.
        if (existing.Status != "ACTIVE")
        {
            return Result<CustomerResponse>.Failure(ServiceError.CustomerInactive);
        }

        var phoneNormalized = PhoneNormalizer.Normalize(request.Phone);

        // The customer's own row holds this number already, so it is excluded -
        // otherwise saving the form without touching the phone would clash with itself.
        if (await _repository.ActivePhoneExistsAsync(phoneNormalized, excludeCustomerId: id))
        {
            return Result<CustomerResponse>.Failure(ServiceError.DuplicatePhone);
        }

        var customer = new Customer
        {
            // Identity and lifecycle stay as they were; only the editable fields
            // come from the request.
            Id = existing.Id,
            Status = existing.Status,
            CreatedAt = existing.CreatedAt,
            Name = request.Name,
            Phone = request.Phone,
            PhoneNormalized = phoneNormalized,
            Address = request.Address,
            CustomerType = request.CustomerType,
            Email = request.Email
        };

        try
        {
            await _repository.UpdateAsync(customer);
        }
        catch (MySqlException ex) when (ex.Number == DuplicateEntryErrorNumber)
        {
            // Same race as on create: two requests can both clear the check above
            // before either one writes, and the unique index is what settles it.
            return Result<CustomerResponse>.Failure(ServiceError.DuplicatePhone);
        }

        // Read back so updated_at is the value the database wrote, not a stale one.
        var updated = await _repository.GetByIdAsync(customer.Id) ?? customer;

        return Result<CustomerResponse>.Success(MapToResponse(updated));
    }

    public async Task<Result<CustomerResponse>> DeactivateAsync(string id)
    {
        var existing = await _repository.GetByIdAsync(id);

        if (existing is null)
        {
            return Result<CustomerResponse>.Failure(ServiceError.NotFound);
        }

        // Deactivating an already-inactive customer is the same answer as
        // deactivating an active one, so it succeeds - but without a write.
        // Writing redundantly would move updated_at and make a no-op look like
        // a modification.
        if (existing.Status != "ACTIVE")
        {
            return Result<CustomerResponse>.Success(MapToResponse(existing));
        }

        await _repository.DeactivateAsync(id);

        // Read back so status and updated_at are the values the database holds.
        var deactivated = await _repository.GetByIdAsync(id) ?? existing;

        return Result<CustomerResponse>.Success(MapToResponse(deactivated));
    }

    public async Task<CustomerResponse?> GetByIdAsync(string id)
    {
        var customer = await _repository.GetByIdAsync(id);

        return customer is null ? null : MapToResponse(customer);
    }

    public async Task<List<CustomerResponse>> GetAllAsync()
    {
        var customers = await _repository.GetAllAsync();

        return customers.Select(MapToResponse).ToList();
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
