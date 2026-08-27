using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Services;

namespace CustomerAssetService.Tests;

public class AssetServiceTests
{
    private const string CustomerId = "11111111-1111-1111-1111-111111111111";

    private static Customer ActiveCustomer() => new()
    {
        Id = CustomerId,
        Name = "Nimal Perera",
        Phone = "077-111-2222",
        PhoneNormalized = "0771112222",
        Address = "12 Galle Road, Colombo 03",
        CustomerType = "INDIVIDUAL",
        Status = "ACTIVE",
        CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
    };

    private static CreateAssetRequest ValidRequest() => new()
    {
        CustomerId = CustomerId,
        AssetType = "AIR_CONDITIONER",
        Model = "CoolMax 12",
        SerialNumber = "ABC-123",
        InstallationDate = new DateOnly(2024, 6, 1),
        Location = "Living room",
        Notes = "fitted upstairs",
    };

    private const string AssetId = "22222222-2222-2222-2222-222222222222";

    private static Asset ExistingAsset() => new()
    {
        Id = AssetId,
        CustomerId = CustomerId,
        AssetType = "AIR_CONDITIONER",
        Model = "CoolMax 12",
        SerialNumber = "ABC-123",
        SerialNormalized = "ABC123",
        InstallationDate = new DateOnly(2024, 6, 1),
        Location = "Living room",
        Notes = "fitted upstairs",
        CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
    };

    private static UpdateAssetRequest ValidUpdateRequest() => new()
    {
        AssetType = "REFRIGERATOR",
        Model = "FrostFree 300",
        SerialNumber = "XYZ-999",
        InstallationDate = new DateOnly(2023, 1, 15),
        Location = "Kitchen",
        Notes = "moved downstairs",
    };

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesAsset()
    {
        // Arrange - SerialExistsResult is left false, so the pre-check passes.
        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);

        var written = Assert.IsType<Asset>(assets.CreatedAsset);
        // The id is the server's, not the client's - the request has no id field
        // at all, so a real GUID here is the only acceptable outcome.
        Assert.True(Guid.TryParse(written.Id, out _));
        Assert.Equal(CustomerId, written.CustomerId);
        Assert.Equal("AIR_CONDITIONER", written.AssetType);
        Assert.Equal("CoolMax 12", written.Model);
        Assert.Equal("ABC-123", written.SerialNumber);
        Assert.Equal(new DateOnly(2024, 6, 1), written.InstallationDate);
        Assert.Equal("Living room", written.Location);
        Assert.Equal("fitted upstairs", written.Notes);

        // The response carries the id the service generated, so the controller's
        // Location header points at a row that exists.
        Assert.Equal(written.Id, result.Value!.Id);
        Assert.Equal("ABC-123", result.Value.SerialNumber);
    }

    [Fact]
    public async Task CreateAsync_NormalizesSerial_WhileKeepingWhatWasTyped()
    {
        // Arrange
        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        var request = ValidRequest();
        request.SerialNumber = "abc 123";

        // Act
        var result = await service.CreateAsync(request);

        // Assert - both forms are stored: the normalized one is what the unique
        // index compares, the typed one is what the Agent gets back.
        Assert.Equal("ABC123", assets.CreatedAsset!.SerialNormalized);
        Assert.Equal("abc 123", assets.CreatedAsset.SerialNumber);
        Assert.Equal("abc 123", result.Value!.SerialNumber);

        // The duplicate check runs against the normalized form, not the raw one -
        // otherwise "abc 123" and "ABC-123" would not be seen as the same unit.
        Assert.Equal("ABC123", assets.SerialExistsSerialNormalized);
    }

    [Fact]
    public async Task CreateAsync_WhenCustomerMissing_ReturnsCustomerNotFoundWithoutInserting()
    {
        // Arrange - CustomerToReturn is left null, standing in for no such row.
        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository();
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.CustomerNotFound, result.Error);
        Assert.Equal(0, assets.CreateAsyncCallCount);
        // The owner is checked before the serial is: there is no point asking
        // the database about a serial for an asset that cannot be created.
        Assert.Equal(0, assets.SerialExistsAsyncCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenCustomerInactive_ReturnsCustomerInactiveWithoutInserting()
    {
        // Arrange
        var inactive = ActiveCustomer();
        inactive.Status = "INACTIVE";

        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository { CustomerToReturn = inactive };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert - a deactivated customer is history; new equipment cannot be
        // attached to one, and it is told apart from a customer that never
        // existed so the Agent knows which mistake they made.
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.CustomerInactive, result.Error);
        Assert.Equal(0, assets.CreateAsyncCallCount);
        Assert.Equal(0, assets.SerialExistsAsyncCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenSerialExists_FailsWithoutInserting()
    {
        // Arrange
        var assets = new FakeAssetRepository { SerialExistsResult = true };
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicateSerial, result.Error);
        // The valuable one: the pre-check short-circuits, so the insert is
        // never attempted rather than being attempted and rejected.
        Assert.Equal(0, assets.CreateAsyncCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenUniqueIndexRejectsTheInsert_ReturnsDuplicateSerial()
    {
        // Arrange - the pre-check passes and the insert still fails, which is
        // the race the unique index exists to settle: two requests can both
        // clear SerialExistsAsync before either one inserts.
        var assets = new FakeAssetRepository
        {
            SerialExistsResult = false,
            ExceptionToThrow = MySqlExceptions.DuplicateKey(
                "Duplicate entry 'ABC123' for key 'uq_assets_serial_normalized'"),
        };
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert - the same answer the pre-check would have given, so the loser
        // of the race sees a duplicate-serial error and not a 500.
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicateSerial, result.Error);
        // Unlike the pre-check case, the insert was attempted - that is what
        // distinguishes the race from the ordinary duplicate.
        Assert.Equal(1, assets.CreateAsyncCallCount);
    }

    [Fact]
    public async Task CreateAsync_ReadsTheRowBack_SoTimestampsAreTheDatabases()
    {
        // Arrange - created_at and updated_at are database defaults, so the
        // service re-reads rather than returning default(DateTime) for both.
        var stored = new Asset
        {
            Id = "22222222-2222-2222-2222-222222222222",
            CustomerId = CustomerId,
            AssetType = "AIR_CONDITIONER",
            Model = "CoolMax 12",
            SerialNumber = "ABC-123",
            SerialNormalized = "ABC123",
            InstallationDate = new DateOnly(2024, 6, 1),
            Location = "Living room",
            Notes = "fitted upstairs",
            CreatedAt = new DateTime(2026, 8, 27, 9, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 27, 9, 30, 0, DateTimeKind.Utc),
        };

        var assets = new FakeAssetRepository { AssetToReturn = stored };
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.CreateAsync(ValidRequest());

        // Assert - the response is the row as the database holds it, and the
        // row read back is the one just written.
        Assert.Equal(assets.CreatedAsset!.Id, assets.GetByIdId);
        Assert.Equal(stored.CreatedAt, result.Value!.CreatedAt);
        Assert.Equal(stored.UpdatedAt, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_WhenAssetExists_ReturnsMappedResponse()
    {
        // Arrange
        var asset = new Asset
        {
            Id = "22222222-2222-2222-2222-222222222222",
            CustomerId = CustomerId,
            AssetType = "REFRIGERATOR",
            Model = "FrostFree 300",
            SerialNumber = "XYZ-999",
            SerialNormalized = "XYZ999",
            InstallationDate = new DateOnly(2023, 1, 15),
            Location = "Kitchen",
            Notes = null,
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc),
        };

        var assets = new FakeAssetRepository { AssetToReturn = asset };
        var service = new AssetService(assets, new FakeCustomerRepository());

        // Act
        var response = await service.GetByIdAsync(asset.Id);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(asset.Id, response.Id);
        Assert.Equal(asset.CustomerId, response.CustomerId);
        Assert.Equal(asset.AssetType, response.AssetType);
        Assert.Equal(asset.Model, response.Model);
        Assert.Equal(asset.SerialNumber, response.SerialNumber);
        Assert.Equal(asset.InstallationDate, response.InstallationDate);
        Assert.Equal(asset.Location, response.Location);
        Assert.Null(response.Notes);
        Assert.Equal(asset.CreatedAt, response.CreatedAt);
        Assert.Equal(asset.UpdatedAt, response.UpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_WhenAssetMissing_ReturnsNull()
    {
        // Arrange - AssetToReturn is left null, standing in for no such row.
        var assets = new FakeAssetRepository();
        var service = new AssetService(assets, new FakeCustomerRepository());

        // Act
        var response = await service.GetByIdAsync("22222222-2222-2222-2222-222222222222");

        // Assert - null rather than a throw, so the controller turns it into a
        // 404 without catching anything.
        Assert.Null(response);
        Assert.Equal("22222222-2222-2222-2222-222222222222", assets.GetByIdId);
    }

    [Fact]
    public async Task GetByCustomerIdAsync_WhenCustomerHasAssets_ReturnsMappedResponses()
    {
        // Arrange
        var first = new Asset
        {
            Id = "22222222-2222-2222-2222-222222222222",
            CustomerId = CustomerId,
            AssetType = "REFRIGERATOR",
            Model = "FrostFree 300",
            SerialNumber = "XYZ-999",
            SerialNormalized = "XYZ999",
            InstallationDate = new DateOnly(2023, 1, 15),
            Location = "Kitchen",
            Notes = null,
            CreatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 11, 0, 0, DateTimeKind.Utc),
        };
        var second = new Asset
        {
            Id = "33333333-3333-3333-3333-333333333333",
            CustomerId = CustomerId,
            AssetType = "AIR_CONDITIONER",
            Model = "CoolMax 12",
            SerialNumber = "ABC-123",
            SerialNormalized = "ABC123",
            InstallationDate = new DateOnly(2024, 6, 1),
            Location = "Living room",
            Notes = "fitted upstairs",
            CreatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
        };

        var assets = new FakeAssetRepository { AssetsToReturn = new List<Asset> { first, second } };
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.GetByCustomerIdAsync(CustomerId);

        // Assert - the repository's order is kept as it came, and the id asked
        // for is the one the caller passed in.
        Assert.True(result.IsSuccess);
        Assert.Equal(CustomerId, assets.GetByCustomerIdCustomerId);
        Assert.Equal(2, result.Value!.Count);

        Assert.Equal(first.Id, result.Value[0].Id);
        Assert.Equal(first.CustomerId, result.Value[0].CustomerId);
        Assert.Equal(first.AssetType, result.Value[0].AssetType);
        Assert.Equal(first.Model, result.Value[0].Model);
        Assert.Equal(first.SerialNumber, result.Value[0].SerialNumber);
        Assert.Equal(first.InstallationDate, result.Value[0].InstallationDate);
        Assert.Equal(first.Location, result.Value[0].Location);
        Assert.Null(result.Value[0].Notes);
        Assert.Equal(first.CreatedAt, result.Value[0].CreatedAt);
        Assert.Equal(first.UpdatedAt, result.Value[0].UpdatedAt);

        Assert.Equal(second.Id, result.Value[1].Id);
        Assert.Equal(second.SerialNumber, result.Value[1].SerialNumber);
        Assert.Equal(second.Notes, result.Value[1].Notes);
    }

    [Fact]
    public async Task GetByCustomerIdAsync_WhenCustomerMissing_ReturnsCustomerNotFound()
    {
        // Arrange - CustomerToReturn is left null, standing in for no such customer.
        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository();
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.GetByCustomerIdAsync(CustomerId);

        // Assert - the customer is checked first, so the asset list is never
        // asked for and the controller has a missing customer to turn into a 404.
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.CustomerNotFound, result.Error);
        Assert.Null(assets.GetByCustomerIdCustomerId);
    }

    [Fact]
    public async Task GetByCustomerIdAsync_WhenCustomerHasNoAssets_ReturnsEmptyList()
    {
        // Arrange - AssetsToReturn is left at its empty default.
        var assets = new FakeAssetRepository();
        var customers = new FakeCustomerRepository { CustomerToReturn = ActiveCustomer() };
        var service = new AssetService(assets, customers);

        // Act
        var result = await service.GetByCustomerIdAsync(CustomerId);

        // Assert - an empty list, never null: the controller returns 200 with an
        // empty array rather than a 404 when the customer owns nothing.
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task UpdateAsync_WithValidRequest_ChangesEditableFieldsOnly()
    {
        // Arrange - an asset to edit, and no clash on the new serial.
        var existing = ExistingAsset();
        var assets = new FakeAssetRepository { AssetToReturn = existing };
        var service = new AssetService(assets, new FakeCustomerRepository());
        var request = ValidUpdateRequest();

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);
        Assert.Equal(1, assets.UpdateAsyncCallCount);

        var updated = assets.UpdatedAsset;
        Assert.NotNull(updated);
        Assert.Equal(request.AssetType, updated!.AssetType);
        Assert.Equal(request.Model, updated.Model);
        // The serial is stored as typed, alongside the normalized form.
        Assert.Equal("XYZ-999", updated.SerialNumber);
        Assert.Equal("XYZ999", updated.SerialNormalized);
        Assert.Equal(request.InstallationDate, updated.InstallationDate);
        Assert.Equal(request.Location, updated.Location);
        Assert.Equal(request.Notes, updated.Notes);
        // The three the request may not touch are carried over unchanged - an
        // asset does not change hands, and its creation time is history.
        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal(existing.CustomerId, updated.CustomerId);
        Assert.Equal(existing.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateAsync_WhenAssetMissing_ReturnsNotFoundWithoutUpdating()
    {
        // Arrange - AssetToReturn is left null, standing in for no such row.
        var assets = new FakeAssetRepository();
        var service = new AssetService(assets, new FakeCustomerRepository());

        // Act
        var result = await service.UpdateAsync("00000000-0000-0000-0000-000000000000", ValidUpdateRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Equal(0, assets.UpdateAsyncCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenAnotherAssetHasSerial_FailsWithoutUpdating()
    {
        // Arrange - the new serial is held by another asset, so the pre-check
        // hits: the exclusion only takes this asset's own row out of the query.
        var existing = ExistingAsset();
        var assets = new FakeAssetRepository
        {
            AssetToReturn = existing,
            SerialExistsResult = true,
        };
        var service = new AssetService(assets, new FakeCustomerRepository());

        // Act
        var result = await service.UpdateAsync(existing.Id, ValidUpdateRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicateSerial, result.Error);
        // The pre-check short-circuits, so the update is never attempted.
        Assert.Equal(0, assets.UpdateAsyncCallCount);
    }

    [Fact]
    public async Task UpdateAsync_WhenSerialUnchanged_ExcludesOwnRowAndSucceeds()
    {
        // Arrange - a clash is configured, so without the exclusion the asset
        // would be rejected for holding the serial it already holds.
        var existing = ExistingAsset();
        var assets = new FakeAssetRepository
        {
            AssetToReturn = existing,
            SerialExistsResult = true,
        };
        var service = new AssetService(assets, new FakeCustomerRepository());
        var request = ValidUpdateRequest();
        // Only the location changes; the serial is the same one, retyped.
        request.AssetType = existing.AssetType;
        request.Model = existing.Model;
        request.SerialNumber = "abc 123";
        request.InstallationDate = existing.InstallationDate;
        request.Location = "Bedroom";
        request.Notes = existing.Notes;

        // Act
        var result = await service.UpdateAsync(existing.Id, request);

        // Assert - this is the one that matters: the asset's own id reaches the
        // repository as the row to exclude, so it does not clash with itself.
        Assert.Equal(existing.Id, assets.SerialExistsExcludeAssetId);
        Assert.Equal("ABC123", assets.SerialExistsSerialNormalized);
        Assert.True(result.IsSuccess);
        Assert.Equal(ServiceError.None, result.Error);
        Assert.Equal(1, assets.UpdateAsyncCallCount);
        Assert.Equal("Bedroom", assets.UpdatedAsset!.Location);
        // Stored as typed even when only the punctuation changed.
        Assert.Equal("abc 123", assets.UpdatedAsset.SerialNumber);
    }

    [Fact]
    public async Task UpdateAsync_WhenUniqueIndexRejectsTheUpdate_ReturnsDuplicateSerial()
    {
        // Arrange - the same race on the update path: the pre-check clears and
        // the unique index is what settles it once the write lands.
        var existing = ExistingAsset();
        var assets = new FakeAssetRepository
        {
            AssetToReturn = existing,
            SerialExistsResult = false,
            ExceptionToThrow = MySqlExceptions.DuplicateKey(
                "Duplicate entry 'XYZ999' for key 'uq_assets_serial_normalized'"),
        };
        var service = new AssetService(assets, new FakeCustomerRepository());

        // Act
        var result = await service.UpdateAsync(existing.Id, ValidUpdateRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceError.DuplicateSerial, result.Error);
        // Unlike the pre-check case above, the update was attempted.
        Assert.Equal(1, assets.UpdateAsyncCallCount);
    }
}
