using CustomerAssetService.Repositories;

namespace CustomerAssetService.Services;

public sealed class StaffAccountMigrationRunner
{
    private const string MigrationName = "V04__create_staff_accounts.sql";
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffAccountMigrationRunner(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task ApplyAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "migrations", MigrationName);
        if (!File.Exists(path)) throw new FileNotFoundException("The embedded StaffAccount migration was not published.", path);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using (var tracking = connection.CreateCommand())
        {
            tracking.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    filename VARCHAR(255) NOT NULL,
                    applied_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (filename)
                );";
            await tracking.ExecuteNonQueryAsync();
        }

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE filename = @filename;";
            check.Parameters.AddWithValue("@filename", MigrationName);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) return;
        }

        await using (var migration = connection.CreateCommand())
        {
            migration.CommandText = await File.ReadAllTextAsync(path);
            await migration.ExecuteNonQueryAsync();
        }

        await using var record = connection.CreateCommand();
        record.CommandText = "INSERT INTO schema_migrations (filename) VALUES (@filename);";
        record.Parameters.AddWithValue("@filename", MigrationName);
        await record.ExecuteNonQueryAsync();
    }
}
