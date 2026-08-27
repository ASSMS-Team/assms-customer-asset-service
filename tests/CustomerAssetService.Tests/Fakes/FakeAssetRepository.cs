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
    // Left null for the happy path. Set it to make CreateAsync throw - a
    // MySqlException with number 1062 is what the service treats as the
    // duplicate-serial race the unique index catches.
    public Exception? ExceptionToThrow;

    // GetByIdAsync - left null so an unconfigured fake stands in for "no such
    // row". On the create path the service falls back to the asset it just
    // built when this is null, so the happy path needs no configuration.
    public string? GetByIdId;
    public Asset? AssetToReturn;

    // SerialExistsAsync
    public string? SerialExistsSerialNormalized;
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

    public Task<bool> SerialExistsAsync(string serialNormalized)
    {
        SerialExistsSerialNormalized = serialNormalized;
        SerialExistsAsyncCallCount++;

        return Task.FromResult(SerialExistsResult);
    }
}
