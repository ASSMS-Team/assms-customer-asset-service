using CustomerAssetService.Repositories;

namespace CustomerAssetService.Services;

public sealed class StaffAccountMigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffAccountMigrationRunner(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task ApplyAsync()
    {
        await ApplyMigrationAsync("V04__create_staff_accounts.sql");
        await ApplyMigrationAsync("V05__link_technician_accounts.sql");
    }

    private async Task ApplyMigrationAsync(string migrationName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "migrations", migrationName);
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
            check.Parameters.AddWithValue("@filename", migrationName);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) return;
        }

        if (migrationName == "V05__link_technician_accounts.sql")
        {
            // DDL commits independently of the tracking insert. Check each part
            // so a retry after interruption safely repairs a partial migration.
            await using var exists = connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'staff_accounts' AND column_name = 'technician_id';";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0)
            {
                await using var add = connection.CreateCommand();
                add.CommandText = "ALTER TABLE staff_accounts ADD COLUMN technician_id CHAR(36) NULL;";
                await add.ExecuteNonQueryAsync();
            }
            exists.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'staff_accounts' AND index_name = 'uq_staff_accounts_technician';";
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0)
            {
                await using var add = connection.CreateCommand();
                add.CommandText = "ALTER TABLE staff_accounts ADD UNIQUE KEY uq_staff_accounts_technician (technician_id);";
                await add.ExecuteNonQueryAsync();
            }
        }
        else
        {
            await using var migration = connection.CreateCommand();
            migration.CommandText = await File.ReadAllTextAsync(path);
            await migration.ExecuteNonQueryAsync();
        }

        await using var record = connection.CreateCommand();
        record.CommandText = "INSERT INTO schema_migrations (filename) VALUES (@filename);";
        record.Parameters.AddWithValue("@filename", migrationName);
        await record.ExecuteNonQueryAsync();
    }
}
