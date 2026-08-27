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
}
