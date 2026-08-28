using CustomerAssetService.DTOs;
using CustomerAssetService.Extensions;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using MySqlConnector;

namespace CustomerAssetService.Services;

public class AssetService
{
    private const int DuplicateEntryErrorNumber = 1062;

    private readonly IAssetRepository _repository;
    // The owning customer has to exist and be active before an asset can be
    // registered against it, and that is a question only the customer
    // repository can answer - hence the second dependency.
    private readonly ICustomerRepository _customerRepository;

    public AssetService(IAssetRepository repository, ICustomerRepository customerRepository)
    {
        _repository = repository;
        _customerRepository = customerRepository;
    }

    public async Task<Result<AssetResponse>> CreateAsync(CreateAssetRequest request)
    {
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId);

        if (customer is null)
        {
            return Result<AssetResponse>.Failure(ServiceError.CustomerNotFound);
        }

        // A deactivated customer is history. Attaching new equipment to one would
        // create a record no one is servicing, so the reference is refused rather
        // than quietly reactivating anything.
        if (customer.Status != "ACTIVE")
        {
            return Result<AssetResponse>.Failure(ServiceError.CustomerInactive);
        }

        var serialNormalized = SerialNormalizer.Normalize(request.SerialNumber);

        if (await _repository.SerialExistsAsync(serialNormalized))
        {
            return Result<AssetResponse>.Failure(ServiceError.DuplicateSerial);
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = request.CustomerId,
            AssetType = request.AssetType,
            Model = request.Model,
            SerialNumber = request.SerialNumber,
            SerialNormalized = serialNormalized,
            // [Required] has already rejected a null before the action ran, so
            // the value is present by the time the service sees the request.
            InstallationDate = request.InstallationDate!.Value,
            Location = request.Location,
            Notes = request.Notes,
            // Not sent to the database - the column's DEFAULT 'ACTIVE' is what
            // actually writes it. It is set here so the fallback below has the
            // status the row will hold, rather than an empty string, if the
            // read-back misses.
            Status = "ACTIVE"
        };

        try
        {
            await _repository.CreateAsync(asset);
        }
        catch (MySqlException ex) when (ex.Number == DuplicateEntryErrorNumber)
        {
            // The unique index is what actually enforces this: two requests can
            // both clear the check above before either one inserts.
            return Result<AssetResponse>.Failure(ServiceError.DuplicateSerial);
        }

        // created_at and updated_at are database defaults, so the row is read
        // back rather than returning default(DateTime) for both.
        var created = await _repository.GetByIdAsync(asset.Id) ?? asset;

        return Result<AssetResponse>.Success(MapToResponse(created));
    }

    public async Task<Result<AssetResponse>> UpdateAsync(string id, UpdateAssetRequest request)
    {
        var existing = await _repository.GetByIdAsync(id);

        if (existing is null)
        {
            return Result<AssetResponse>.Failure(ServiceError.NotFound);
        }

        // Only ACTIVE assets are editable - a deactivated asset is the record of
        // a unit that is no longer in service, and rewriting it would change what
        // that unit was. Checked before anything else runs, so a refused edit
        // costs no serial lookup.
        if (existing.Status != "ACTIVE")
        {
            return Result<AssetResponse>.Failure(ServiceError.AssetInactive);
        }

        var serialNormalized = SerialNormalizer.Normalize(request.SerialNumber);

        // The asset's own row holds this serial already, so it is excluded -
        // otherwise saving the form without touching the serial would clash with itself.
        if (await _repository.SerialExistsAsync(serialNormalized, excludeAssetId: id))
        {
            return Result<AssetResponse>.Failure(ServiceError.DuplicateSerial);
        }

        var asset = new Asset
        {
            // Identity and ownership stay as they were; only the editable fields
            // come from the request.
            Id = existing.Id,
            CustomerId = existing.CustomerId,
            CreatedAt = existing.CreatedAt,
            AssetType = request.AssetType,
            Model = request.Model,
            SerialNumber = request.SerialNumber,
            SerialNormalized = serialNormalized,
            // [Required] has already rejected a null before the action ran, so
            // the value is present by the time the service sees the request.
            InstallationDate = request.InstallationDate!.Value,
            Location = request.Location,
            Notes = request.Notes
        };

        try
        {
            await _repository.UpdateAsync(asset);
        }
        catch (MySqlException ex) when (ex.Number == DuplicateEntryErrorNumber)
        {
            // Same race as on create: two requests can both clear the check above
            // before either one writes, and the unique index is what settles it.
            return Result<AssetResponse>.Failure(ServiceError.DuplicateSerial);
        }

        // Read back so updated_at is the value the database wrote, not a stale one.
        var updated = await _repository.GetByIdAsync(asset.Id) ?? asset;

        return Result<AssetResponse>.Success(MapToResponse(updated));
    }

    public async Task<Result<AssetResponse>> DeactivateAsync(string id)
    {
        var existing = await _repository.GetByIdAsync(id);

        if (existing is null)
        {
            return Result<AssetResponse>.Failure(ServiceError.NotFound);
        }

        // Deactivating an already-inactive asset is the same answer as
        // deactivating an active one, so it succeeds - but without a write.
        // Writing redundantly would move updated_at and make a no-op look like
        // a modification.
        if (existing.Status != "ACTIVE")
        {
            return Result<AssetResponse>.Success(MapToResponse(existing));
        }

        await _repository.DeactivateAsync(id);

        // Read back so status and updated_at are the values the database holds.
        var deactivated = await _repository.GetByIdAsync(id) ?? existing;

        return Result<AssetResponse>.Success(MapToResponse(deactivated));
    }

    public async Task<AssetResponse?> GetByIdAsync(string id)
    {
        var asset = await _repository.GetByIdAsync(id);

        return asset is null ? null : MapToResponse(asset);
    }

    public async Task<Result<List<AssetResponse>>> GetByCustomerIdAsync(string customerId)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId);

        if (customer is null)
        {
            return Result<List<AssetResponse>>.Failure(ServiceError.CustomerNotFound);
        }

        // No status check here, unlike the create path: a deactivated customer
        // can take no new equipment, but the equipment it already has is history
        // that should still be viewable.
        var assets = await _repository.GetByCustomerIdAsync(customerId);

        return Result<List<AssetResponse>>.Success(assets.Select(MapToResponse).ToList());
    }

    private static AssetResponse MapToResponse(Asset asset) => new()
    {
        Id = asset.Id,
        CustomerId = asset.CustomerId,
        AssetType = asset.AssetType,
        Model = asset.Model,
        SerialNumber = asset.SerialNumber,
        InstallationDate = asset.InstallationDate,
        Location = asset.Location,
        Notes = asset.Notes,
        Status = asset.Status,
        CreatedAt = asset.CreatedAt,
        UpdatedAt = asset.UpdatedAt
    };
}
