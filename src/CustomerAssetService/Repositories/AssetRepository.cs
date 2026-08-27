using CustomerAssetService.Models;
using MySqlConnector;

namespace CustomerAssetService.Repositories;

public class AssetRepository : IAssetRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AssetRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // created_at and updated_at have database defaults, so neither is written here.
    public async Task CreateAsync(Asset asset)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO assets
                (id, customer_id, asset_type, model, serial_number, serial_normalized,
                 installation_date, location, notes)
            VALUES
                (@id, @customerId, @assetType, @model, @serialNumber, @serialNormalized,
                 @installationDate, @location, @notes);";

        command.Parameters.AddWithValue("@id", asset.Id);
        command.Parameters.AddWithValue("@customerId", asset.CustomerId);
        command.Parameters.AddWithValue("@assetType", asset.AssetType);
        command.Parameters.AddWithValue("@model", asset.Model);
        command.Parameters.AddWithValue("@serialNumber", asset.SerialNumber);
        command.Parameters.AddWithValue("@serialNormalized", asset.SerialNormalized);
        // MySqlConnector takes a DateOnly parameter as-is against a DATE column -
        // no conversion to DateTime is needed on the way in.
        command.Parameters.AddWithValue("@installationDate", asset.InstallationDate);
        command.Parameters.AddWithValue("@location", asset.Location);
        command.Parameters.AddWithValue("@notes", (object?)asset.Notes ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<Asset?> GetByIdAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, customer_id, asset_type, model, serial_number, serial_normalized,
                   installation_date, location, notes, created_at, updated_at
            FROM assets
            WHERE id = @id;";

        command.Parameters.AddWithValue("@id", id);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        // Ordinals are looked up by name so that reordering the SELECT list
        // cannot silently shift the mapping.
        var idOrdinal = reader.GetOrdinal("id");
        var customerIdOrdinal = reader.GetOrdinal("customer_id");
        var assetTypeOrdinal = reader.GetOrdinal("asset_type");
        var modelOrdinal = reader.GetOrdinal("model");
        var serialNumberOrdinal = reader.GetOrdinal("serial_number");
        var serialNormalizedOrdinal = reader.GetOrdinal("serial_normalized");
        var installationDateOrdinal = reader.GetOrdinal("installation_date");
        var locationOrdinal = reader.GetOrdinal("location");
        var notesOrdinal = reader.GetOrdinal("notes");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");

        return new Asset
        {
            // MySqlConnector reads CHAR(36) as a Guid by default (GuidFormat=Char36),
            // so GetString throws on these columns - go through the boxed value instead.
            Id = reader.GetValue(idOrdinal)?.ToString() ?? string.Empty,
            CustomerId = reader.GetValue(customerIdOrdinal)?.ToString() ?? string.Empty,
            AssetType = reader.GetString(assetTypeOrdinal),
            Model = reader.GetString(modelOrdinal),
            SerialNumber = reader.GetString(serialNumberOrdinal),
            SerialNormalized = reader.GetString(serialNormalizedOrdinal),
            // A DATE column reports its field type as DateTime and GetValue boxes
            // one, so GetDateTime would hand back a midnight DateTime. Asking for
            // the value as DateOnly is what makes MySqlConnector do the conversion.
            InstallationDate = reader.GetFieldValue<DateOnly>(installationDateOrdinal),
            Location = reader.GetString(locationOrdinal),
            Notes = reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal),
            CreatedAt = reader.GetDateTime(createdAtOrdinal),
            UpdatedAt = reader.GetDateTime(updatedAtOrdinal)
        };
    }

    public async Task<List<Asset>> GetByCustomerIdAsync(string customerId)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // No LIMIT: this is the customer's whole equipment history, not a page
        // of it. The ORDER BY is not decoration either - without one MySQL
        // guarantees nothing about row order, so the same query can come back
        // shuffled between requests. created_at is a second-precision TIMESTAMP,
        // so two assets registered within the same second would tie and shuffle
        // anyway - id breaks the tie and makes the order total.
        command.CommandText = @"
            SELECT id, customer_id, asset_type, model, serial_number, serial_normalized,
                   installation_date, location, notes, created_at, updated_at
            FROM assets
            WHERE customer_id = @customerId
            ORDER BY created_at DESC, id;";

        command.Parameters.AddWithValue("@customerId", customerId);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync();

        // Looked up by name so that reordering the SELECT list cannot silently
        // shift the mapping, and once up front rather than once per row.
        var idOrdinal = reader.GetOrdinal("id");
        var customerIdOrdinal = reader.GetOrdinal("customer_id");
        var assetTypeOrdinal = reader.GetOrdinal("asset_type");
        var modelOrdinal = reader.GetOrdinal("model");
        var serialNumberOrdinal = reader.GetOrdinal("serial_number");
        var serialNormalizedOrdinal = reader.GetOrdinal("serial_normalized");
        var installationDateOrdinal = reader.GetOrdinal("installation_date");
        var locationOrdinal = reader.GetOrdinal("location");
        var notesOrdinal = reader.GetOrdinal("notes");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");

        var assets = new List<Asset>();

        while (await reader.ReadAsync())
        {
            assets.Add(new Asset
            {
                // MySqlConnector reads CHAR(36) as a Guid by default (GuidFormat=Char36),
                // so GetString throws on these columns - go through the boxed value instead.
                Id = reader.GetValue(idOrdinal)?.ToString() ?? string.Empty,
                CustomerId = reader.GetValue(customerIdOrdinal)?.ToString() ?? string.Empty,
                AssetType = reader.GetString(assetTypeOrdinal),
                Model = reader.GetString(modelOrdinal),
                SerialNumber = reader.GetString(serialNumberOrdinal),
                SerialNormalized = reader.GetString(serialNormalizedOrdinal),
                // A DATE column reports its field type as DateTime and GetValue boxes
                // one, so GetDateTime would hand back a midnight DateTime. Asking for
                // the value as DateOnly is what makes MySqlConnector do the conversion.
                InstallationDate = reader.GetFieldValue<DateOnly>(installationDateOrdinal),
                Location = reader.GetString(locationOrdinal),
                Notes = reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal),
                CreatedAt = reader.GetDateTime(createdAtOrdinal),
                UpdatedAt = reader.GetDateTime(updatedAtOrdinal)
            });
        }

        return assets;
    }

    // customer_id is not in the SET list: an asset does not change hands, and
    // moving one would rewrite whose equipment history it belongs to. created_at
    // never changes, and updated_at is maintained by the column's ON UPDATE default.
    public async Task UpdateAsync(Asset asset)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE assets
            SET asset_type = @assetType,
                model = @model,
                serial_number = @serialNumber,
                serial_normalized = @serialNormalized,
                installation_date = @installationDate,
                location = @location,
                notes = @notes
            WHERE id = @id;";

        command.Parameters.AddWithValue("@assetType", asset.AssetType);
        command.Parameters.AddWithValue("@model", asset.Model);
        command.Parameters.AddWithValue("@serialNumber", asset.SerialNumber);
        command.Parameters.AddWithValue("@serialNormalized", asset.SerialNormalized);
        // MySqlConnector takes a DateOnly parameter as-is against a DATE column -
        // no conversion to DateTime is needed on the way in.
        command.Parameters.AddWithValue("@installationDate", asset.InstallationDate);
        command.Parameters.AddWithValue("@location", asset.Location);
        command.Parameters.AddWithValue("@notes", (object?)asset.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", asset.Id);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> SerialExistsAsync(string serialNormalized, string? excludeAssetId = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        // The exclusion clause is only in the SQL when there is an id to exclude,
        // so the create path runs exactly the query it ran before.
        var sql = @"
            SELECT 1
            FROM assets
            WHERE serial_normalized = @serialNormalized";

        if (excludeAssetId is not null)
        {
            sql += " AND id != @excludeId";
        }

        command.CommandText = sql + @"
            LIMIT 1;";

        command.Parameters.AddWithValue("@serialNormalized", serialNormalized);

        if (excludeAssetId is not null)
        {
            command.Parameters.AddWithValue("@excludeId", excludeAssetId);
        }

        var result = await command.ExecuteScalarAsync();

        return result != null && result != DBNull.Value;
    }
}
