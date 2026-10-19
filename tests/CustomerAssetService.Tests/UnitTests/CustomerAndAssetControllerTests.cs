using CustomerAssetService.Controllers;
using CustomerAssetService.DTOs;
using CustomerAssetService.Models;
using CustomerAssetService.Repositories;
using CustomerAssetService.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomerAssetService.Tests.UnitTests;

// Drives the customer and asset controllers through the real services, so
// every HTTP mapping (status code, error key and message) is pinned.
public sealed class CustomerAndAssetControllerTests
{
    private static readonly DateTime Stored = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    // Returns queued records from GetByIdAsync in order, so a test can make the
    // value read back after a write differ from the one read before it.
    private sealed class SequencedCustomers : ICustomerRepository
    {
        public Queue<Customer?> Reads { get; } = new();
        public bool PhoneExists;
        public string? DeactivatedId;
        public Customer? Updated;
        public List<Customer> All { get; } = [];
        public string? StatusFilter;
        public Task CreateAsync(Customer customer) => Task.CompletedTask;
        public Task<Customer?> GetByIdAsync(string id) => Task.FromResult(Reads.Count > 0 ? Reads.Dequeue() : null);
        public Task<List<Customer>> GetAllAsync(string? status = null) { StatusFilter = status; return Task.FromResult(All); }
        public Task UpdateAsync(Customer customer) { Updated = customer; return Task.CompletedTask; }
        public Task DeactivateAsync(string id) { DeactivatedId = id; return Task.CompletedTask; }
        public Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null) => Task.FromResult(PhoneExists);
    }

    private sealed class SequencedAssets : IAssetRepository
    {
        public Queue<Asset?> Reads { get; } = new();
        public bool SerialExists;
        public List<Asset> ByCustomer { get; } = [];
        public Asset? Created;
        public Task CreateAsync(Asset asset) { Created = asset; return Task.CompletedTask; }
        public Task<Asset?> GetByIdAsync(string id) => Task.FromResult(Reads.Count > 0 ? Reads.Dequeue() : null);
        public Task<List<Asset>> GetByCustomerIdAsync(string customerId) => Task.FromResult(ByCustomer);
        public Task UpdateAsync(Asset asset) => Task.CompletedTask;
        public Task DeactivateAsync(string id) => Task.CompletedTask;
        public Task<bool> SerialExistsAsync(string serialNormalized, string? excludeAssetId = null) => Task.FromResult(SerialExists);
    }

    private static Customer Customer(string status = "ACTIVE", string name = "Nimal Perera") => new()
    {
        Id = "cust-1", Name = name, Phone = "0771112222", PhoneNormalized = "94771112222", Address = "12 Galle Road",
        CustomerType = "INDIVIDUAL", Email = "nimal@example.com", Status = status, CreatedAt = Stored, UpdatedAt = Stored
    };

    private static Asset Asset(string status = "ACTIVE", string model = "Inverter 12000") => new()
    {
        Id = "asset-1", CustomerId = "cust-1", AssetType = "AC", Model = model, SerialNumber = "SN-001",
        SerialNormalized = "SN001", InstallationDate = new DateOnly(2026, 1, 15), Location = "Living room",
        Notes = "Wall mounted", Status = status, CreatedAt = Stored, UpdatedAt = Stored
    };

    private static CreateCustomerRequest CreateCustomerRequest() => new()
    {
        Name = "Nimal Perera", Phone = "0771112222", Address = "12 Galle Road", CustomerType = "INDIVIDUAL", Email = "nimal@example.com"
    };

    private static UpdateCustomerRequest UpdateCustomerRequest() => new()
    {
        Name = "Nimal P.", Phone = "0771112222", Address = "14 Galle Road", CustomerType = "INDIVIDUAL", Email = "np@example.com"
    };

    private static CreateAssetRequest CreateAssetRequest() => new()
    {
        CustomerId = "cust-1", AssetType = "AC", Model = "Inverter 12000", SerialNumber = "SN-001",
        InstallationDate = new DateOnly(2026, 1, 15), Location = "Living room", Notes = "Wall mounted"
    };

    private static UpdateAssetRequest UpdateAssetRequest() => new()
    {
        AssetType = "AC", Model = "Inverter 18000", SerialNumber = "SN-001",
        InstallationDate = new DateOnly(2026, 1, 15), Location = "Bedroom", Notes = null
    };

    private static CustomersController CustomersFor(SequencedCustomers customers, SequencedAssets? assets = null) =>
        new(new CustomerService(customers), new AssetService(assets ?? new SequencedAssets(), customers));

    private static AssetsController AssetsFor(SequencedAssets assets, SequencedCustomers customers) =>
        new(new AssetService(assets, customers));

    private static void AssertFieldError(IActionResult result, int status, string key, string message)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Equal(status, problem.Status);
        var error = Assert.Single(problem.Errors);
        Assert.Equal(key, error.Key);
        Assert.Equal(message, Assert.Single(error.Value));
    }

    // ---------- Customers ----------

    [Fact]
    public async Task CreateCustomer_ReturnsCreatedAtGetById_WithTheStoredCustomer()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer(name: "Stored Name"));

        var result = Assert.IsType<CreatedAtActionResult>(await CustomersFor(customers).Create(CreateCustomerRequest()));

        Assert.Equal(nameof(CustomersController.GetById), result.ActionName);
        var body = Assert.IsType<CustomerResponse>(result.Value);
        Assert.Equal("Stored Name", body.Name);
        Assert.Equal(Stored, body.CreatedAt);
        Assert.Equal(body.Id, result.RouteValues!["id"]);
    }

    [Fact]
    public async Task CreateCustomer_FallsBackToTheNewCustomer_WhenTheReadBackFindsNothing()
    {
        var result = Assert.IsType<CreatedAtActionResult>(await CustomersFor(new SequencedCustomers()).Create(CreateCustomerRequest()));

        var body = Assert.IsType<CustomerResponse>(result.Value);
        Assert.Equal("Nimal Perera", body.Name);
        Assert.Equal("ACTIVE", body.Status);
    }

    [Fact]
    public async Task CreateCustomer_ReturnsPhoneConflict_WhenPhoneAlreadyActive()
    {
        var result = await CustomersFor(new SequencedCustomers { PhoneExists = true }).Create(CreateCustomerRequest());

        AssertFieldError(result, 409, "phone", "An active customer already exists with this phone number.");
    }

    [Fact]
    public async Task UpdateCustomer_ReturnsOk_WithTheCustomerReadBackAfterTheWrite()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());
        customers.Reads.Enqueue(Customer(name: "Re-read Name"));

        var ok = Assert.IsType<OkObjectResult>(await CustomersFor(customers).Update("cust-1", UpdateCustomerRequest()));

        Assert.Equal("Re-read Name", Assert.IsType<CustomerResponse>(ok.Value).Name);
        Assert.Equal("Nimal P.", customers.Updated!.Name);
    }

    [Fact]
    public async Task UpdateCustomer_FallsBackToTheWrittenCustomer_WhenTheReadBackFindsNothing()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());

        var ok = Assert.IsType<OkObjectResult>(await CustomersFor(customers).Update("cust-1", UpdateCustomerRequest()));

        var body = Assert.IsType<CustomerResponse>(ok.Value);
        Assert.Equal("Nimal P.", body.Name);
        Assert.Equal("14 Galle Road", body.Address);
    }

    [Fact]
    public async Task UpdateCustomer_ReturnsNotFound_ForUnknownCustomer()
    {
        Assert.IsType<NotFoundResult>(await CustomersFor(new SequencedCustomers()).Update("missing", UpdateCustomerRequest()));
    }

    [Fact]
    public async Task UpdateCustomer_ReturnsInactiveProblem_ForDeactivatedCustomer()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer("INACTIVE"));

        var conflict = Assert.IsType<ConflictObjectResult>(await CustomersFor(customers).Update("cust-1", UpdateCustomerRequest()));

        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Customer is not active.", problem.Title);
        Assert.Equal("This customer has been deactivated and can no longer be edited.", problem.Detail);
    }

    [Fact]
    public async Task UpdateCustomer_ReturnsPhoneConflict_WhenAnotherCustomerHasThePhone()
    {
        var customers = new SequencedCustomers { PhoneExists = true };
        customers.Reads.Enqueue(Customer());

        AssertFieldError(await CustomersFor(customers).Update("cust-1", UpdateCustomerRequest()),
            409, "phone", "Another active customer already exists with this phone number.");
    }

    [Fact]
    public async Task DeactivateCustomer_ReturnsOk_WithTheReReadInactiveCustomer()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());
        customers.Reads.Enqueue(Customer("INACTIVE"));

        var ok = Assert.IsType<OkObjectResult>(await CustomersFor(customers).Deactivate("cust-1"));

        Assert.Equal("INACTIVE", Assert.IsType<CustomerResponse>(ok.Value).Status);
        Assert.Equal("cust-1", customers.DeactivatedId);
    }

    [Fact]
    public async Task DeactivateCustomer_FallsBackToTheExistingCustomer_WhenTheReadBackFindsNothing()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());

        var ok = Assert.IsType<OkObjectResult>(await CustomersFor(customers).Deactivate("cust-1"));

        Assert.Equal("cust-1", Assert.IsType<CustomerResponse>(ok.Value).Id);
    }

    [Fact]
    public async Task DeactivateCustomer_ReturnsNotFound_ForUnknownCustomer()
    {
        Assert.IsType<NotFoundResult>(await CustomersFor(new SequencedCustomers()).Deactivate("missing"));
    }

    [Fact]
    public async Task GetAllCustomers_PassesTheStatusFilterAndReturnsOk()
    {
        var customers = new SequencedCustomers();
        customers.All.Add(Customer("INACTIVE"));

        var ok = Assert.IsType<OkObjectResult>(await CustomersFor(customers).GetAll("INACTIVE"));

        Assert.Equal("INACTIVE", customers.StatusFilter);
        Assert.Equal("cust-1", Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerResponse>>(ok.Value)).Id);
    }

    [Fact]
    public async Task GetCustomerAssets_ReturnsOk_ForKnownCustomer_AndNotFoundOtherwise()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());
        var assets = new SequencedAssets();
        assets.ByCustomer.Add(Asset());
        var controller = CustomersFor(customers, assets);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetAssets("cust-1"));
        Assert.Equal("asset-1", Assert.Single(Assert.IsAssignableFrom<IEnumerable<AssetResponse>>(ok.Value)).Id);
        Assert.IsType<NotFoundResult>(await controller.GetAssets("missing"));
    }

    [Fact]
    public async Task GetCustomerById_ReturnsOk_ForKnownCustomer_AndNotFoundOtherwise()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());
        var controller = CustomersFor(customers);

        Assert.Equal("cust-1", Assert.IsType<CustomerResponse>(Assert.IsType<OkObjectResult>(await controller.GetById("cust-1")).Value).Id);
        Assert.IsType<NotFoundResult>(await controller.GetById("missing"));
    }

    // ---------- Assets ----------

    [Fact]
    public async Task CreateAsset_ReturnsCreatedAtGetById_WithTheStoredActiveAsset()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset(model: "Stored Model"));

        var created = Assert.IsType<CreatedAtActionResult>(await AssetsFor(assets, customers).Create(CreateAssetRequest()));

        Assert.Equal(nameof(AssetsController.GetById), created.ActionName);
        var body = Assert.IsType<AssetResponse>(created.Value);
        Assert.Equal("Stored Model", body.Model);
        Assert.Equal(body.Id, created.RouteValues!["id"]);
        Assert.Equal("ACTIVE", assets.Created!.Status);
    }

    [Fact]
    public async Task CreateAsset_FallsBackToTheNewAsset_WhenTheReadBackFindsNothing()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());

        var created = Assert.IsType<CreatedAtActionResult>(await AssetsFor(new SequencedAssets(), customers).Create(CreateAssetRequest()));

        var body = Assert.IsType<AssetResponse>(created.Value);
        Assert.Equal("Inverter 12000", body.Model);
        Assert.Equal("ACTIVE", body.Status);
    }

    [Fact]
    public async Task CreateAsset_ReturnsCustomerIdError_ForUnknownCustomer()
    {
        AssertFieldError(await AssetsFor(new SequencedAssets(), new SequencedCustomers()).Create(CreateAssetRequest()),
            409, "customerId", "No customer exists with this id.");
    }

    [Fact]
    public async Task CreateAsset_ReturnsCustomerIdError_ForInactiveCustomer()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer("INACTIVE"));

        AssertFieldError(await AssetsFor(new SequencedAssets(), customers).Create(CreateAssetRequest()),
            409, "customerId", "This customer has been deactivated and cannot have new assets registered.");
    }

    [Fact]
    public async Task CreateAsset_ReturnsSerialError_ForDuplicateSerial()
    {
        var customers = new SequencedCustomers();
        customers.Reads.Enqueue(Customer());

        AssertFieldError(await AssetsFor(new SequencedAssets { SerialExists = true }, customers).Create(CreateAssetRequest()),
            409, "serialNumber", "An asset already exists with this serial number.");
    }

    [Fact]
    public async Task UpdateAsset_ReturnsOk_WithTheAssetReadBackAfterTheWrite()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset());
        assets.Reads.Enqueue(Asset(model: "Re-read Model"));

        var ok = Assert.IsType<OkObjectResult>(await AssetsFor(assets, new SequencedCustomers()).Update("asset-1", UpdateAssetRequest()));

        Assert.Equal("Re-read Model", Assert.IsType<AssetResponse>(ok.Value).Model);
    }

    [Fact]
    public async Task UpdateAsset_FallsBackToTheWrittenAsset_WhenTheReadBackFindsNothing()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset());

        var ok = Assert.IsType<OkObjectResult>(await AssetsFor(assets, new SequencedCustomers()).Update("asset-1", UpdateAssetRequest()));

        var body = Assert.IsType<AssetResponse>(ok.Value);
        Assert.Equal("Inverter 18000", body.Model);
        Assert.Equal("Bedroom", body.Location);
    }

    [Fact]
    public async Task UpdateAsset_ReturnsNotFound_ForUnknownAsset()
    {
        Assert.IsType<NotFoundResult>(await AssetsFor(new SequencedAssets(), new SequencedCustomers()).Update("missing", UpdateAssetRequest()));
    }

    [Fact]
    public async Task UpdateAsset_ReturnsInactiveProblem_ForDeactivatedAsset()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset("INACTIVE"));

        var conflict = Assert.IsType<ConflictObjectResult>(await AssetsFor(assets, new SequencedCustomers()).Update("asset-1", UpdateAssetRequest()));

        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Asset is not active.", problem.Title);
        Assert.Equal("This asset has been deactivated and can no longer be edited.", problem.Detail);
    }

    [Fact]
    public async Task UpdateAsset_ReturnsSerialError_ForDuplicateSerial()
    {
        var assets = new SequencedAssets { SerialExists = true };
        assets.Reads.Enqueue(Asset());

        AssertFieldError(await AssetsFor(assets, new SequencedCustomers()).Update("asset-1", UpdateAssetRequest()),
            409, "serialNumber", "Another asset already exists with this serial number.");
    }

    [Fact]
    public async Task DeactivateAsset_ReturnsOk_WithTheReReadInactiveAsset()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset());
        assets.Reads.Enqueue(Asset("INACTIVE"));

        var ok = Assert.IsType<OkObjectResult>(await AssetsFor(assets, new SequencedCustomers()).Deactivate("asset-1"));

        Assert.Equal("INACTIVE", Assert.IsType<AssetResponse>(ok.Value).Status);
    }

    [Fact]
    public async Task DeactivateAsset_FallsBackToTheExistingAsset_WhenTheReadBackFindsNothing()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset());

        var ok = Assert.IsType<OkObjectResult>(await AssetsFor(assets, new SequencedCustomers()).Deactivate("asset-1"));

        Assert.Equal("asset-1", Assert.IsType<AssetResponse>(ok.Value).Id);
    }

    [Fact]
    public async Task DeactivateAsset_ReturnsNotFound_ForUnknownAsset()
    {
        Assert.IsType<NotFoundResult>(await AssetsFor(new SequencedAssets(), new SequencedCustomers()).Deactivate("missing"));
    }

    [Fact]
    public async Task GetAssetById_ReturnsOk_ForKnownAsset_AndNotFoundOtherwise()
    {
        var assets = new SequencedAssets();
        assets.Reads.Enqueue(Asset());
        var controller = AssetsFor(assets, new SequencedCustomers());

        Assert.Equal("asset-1", Assert.IsType<AssetResponse>(Assert.IsType<OkObjectResult>(await controller.GetById("asset-1")).Value).Id);
        Assert.IsType<NotFoundResult>(await controller.GetById("missing"));
    }
}
