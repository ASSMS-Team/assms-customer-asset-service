using CustomerAssetService.Models;
using CustomerAssetService.Repositories;

namespace CustomerAssetService.Tests;

// Hand-rolled stand-in for the real repository: every call records what it was
// given and hands back whatever the test configured, so a service test can run
// without a database.
public class FakeAssetRepository : IAssetRepository
{
    // CreateAsync
    public Asset? CreatedAsset;
    public int CreateAsyncCallCount;
    // Left null for the happy path. Set it to make CreateAsync or UpdateAsync
    // throw - a MySqlException with number 1062 is what the service treats as
    // the duplicate-serial race the unique index catches.
    public Exception? ExceptionToThrow;

    // GetByIdAsync - left null so an unconfigured fake stands in for "no such
    // row". On the create path the service falls back to the asset it just
    // built when this is null, so the happy path needs no configuration.
    public string? GetByIdId;
    public Asset? AssetToReturn;

    // GetByCustomerIdAsync - defaults to an empty list, not null, so an
    // unconfigured fake stands in for "this customer owns nothing yet" rather
    // than blowing up the caller.
    public List<Asset> AssetsToReturn = new();
    // The customer id the list was asked for, so a test can check the service
    // passed the route's id through rather than looking something else up.
    public string? GetByCustomerIdCustomerId;

    // UpdateAsync
    public Asset? UpdatedAsset;
    public int UpdateAsyncCallCount;

    // SerialExistsAsync
    public string? SerialExistsSerialNormalized;
    // Null when the caller passed no id to exclude, which is how a test tells
    // the create path's call apart from the update path's.
    public string? SerialExistsExcludeAssetId;
    public int SerialExistsAsyncCallCount;
    public bool SerialExistsResult;

    public Task CreateAsync(Asset asset)
    {
        // Recorded and counted before the throw, so a test that configures an
        // exception can still assert on what the call was given.
        CreatedAsset = asset;
        CreateAsyncCallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.CompletedTask;
    }

    public Task<Asset?> GetByIdAsync(string id)
    {
        GetByIdId = id;

        return Task.FromResult(AssetToReturn);
    }

    public Task<List<Asset>> GetByCustomerIdAsync(string customerId)
    {
        GetByCustomerIdCustomerId = customerId;

        return Task.FromResult(AssetsToReturn);
    }

    public Task UpdateAsync(Asset asset)
    {
        // Recorded and counted before the throw, for the same reason as CreateAsync.
        UpdatedAsset = asset;
        UpdateAsyncCallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.CompletedTask;
    }

    public Task<bool> SerialExistsAsync(string serialNormalized, string? excludeAssetId = null)
    {
        SerialExistsSerialNormalized = serialNormalized;
        SerialExistsExcludeAssetId = excludeAssetId;
        SerialExistsAsyncCallCount++;

        // Stands in for the WHERE id != @excludeId: the excluded row is out of
        // the query, so an asset still holding its own serial finds no clash
        // even when a clash was configured. Any other serial still clashes.
        if (excludeAssetId is not null
            && excludeAssetId == AssetToReturn?.Id
            && serialNormalized == AssetToReturn.SerialNormalized)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(SerialExistsResult);
    }
}
