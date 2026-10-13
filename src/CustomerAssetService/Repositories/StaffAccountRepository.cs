using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public sealed class StaffAccountRepository : ITechnicianAccountRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffAccountRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    private static StaffAccount Map(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetValue(reader.GetOrdinal("id"))?.ToString() ?? string.Empty,
        FullName = reader.IsDBNull(reader.GetOrdinal("full_name"))
            ? string.Empty : reader.GetString(reader.GetOrdinal("full_name")),
        Username = reader.GetString(reader.GetOrdinal("username")),
        Email = reader.GetString(reader.GetOrdinal("email")),
        PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
        Role = reader.GetString(reader.GetOrdinal("role")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        // MySqlConnector exposes CHAR(36) UUID columns as Guid values.
        // Convert the underlying value as for the staff ID above, rather
        // than calling GetString (which throws for linked accounts).
        TechnicianId = reader.IsDBNull(reader.GetOrdinal("technician_id"))
            ? null : reader.GetValue(reader.GetOrdinal("technician_id")).ToString(),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        CreatedBy = reader.IsDBNull(reader.GetOrdinal("created_by"))
            ? null : reader.GetValue(reader.GetOrdinal("created_by")).ToString()
    };

    private const string SelectColumns =
        "id, full_name, username, email, password_hash, role, status, technician_id, created_at, created_by";

    public async Task<StaffAccount?> FindByIdentifierAsync(string identifier)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT {SelectColumns}
            FROM staff_accounts
            WHERE username_normalized = @identifier OR email_normalized = @identifier
            LIMIT 1;";
        command.Parameters.AddWithValue("@identifier", identifier.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<StaffAccount?> FindByIdAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM staff_accounts WHERE id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<bool> AnyManagerAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM staff_accounts WHERE role = 'Manager');";
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    public async Task<int> CountActiveManagersAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM staff_accounts WHERE role = 'Manager' AND status = 'ACTIVE';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<DeactivateStaffAccountResult> DeactivateAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

        try
        {
            await using var findCmd = connection.CreateCommand();
            findCmd.Transaction = transaction;
            findCmd.CommandText = "SELECT role, status FROM staff_accounts WHERE id = @id LIMIT 1;";
            findCmd.Parameters.AddWithValue("@id", id);

            string? role = null;
            string? status = null;
            await using (var reader = await findCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return DeactivateStaffAccountResult.NotFound;

                role = reader.GetString(reader.GetOrdinal("role"));
                status = reader.GetString(reader.GetOrdinal("status"));
            }

            if (string.Equals(status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                return DeactivateStaffAccountResult.Success;
            }

            if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
            {
                // Lock all active manager rows using FOR UPDATE to prevent race conditions during concurrent deactivations
                await using var countCmd = connection.CreateCommand();
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT id FROM staff_accounts WHERE role = 'Manager' AND status = 'ACTIVE' FOR UPDATE;";

                var activeManagerIds = new List<string>();
                await using (var reader = await countCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var mId = reader.GetValue(0)?.ToString();
                        if (!string.IsNullOrEmpty(mId))
                            activeManagerIds.Add(mId);
                    }
                }

                if (!activeManagerIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    return DeactivateStaffAccountResult.Success;
                }

                if (activeManagerIds.Count <= 1)
                {
                    await transaction.RollbackAsync();
                    return DeactivateStaffAccountResult.LastActiveManager;
                }
            }

            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = "UPDATE staff_accounts SET status = 'INACTIVE' WHERE id = @id;";
            updateCmd.Parameters.AddWithValue("@id", id);
            var affected = await updateCmd.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return affected > 0 ? DeactivateStaffAccountResult.Success : DeactivateStaffAccountResult.NotFound;
        }
        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1213)
        {
            await transaction.RollbackAsync();
            return await DeactivateAsync(id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task CreateAsync(StaffAccount account)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO staff_accounts
                (id, full_name, username, username_normalized, email, email_normalized, password_hash, role, status, technician_id, created_by)
            VALUES
                (@id, @fullName, @username, @usernameNormalized, @email, @emailNormalized, @passwordHash, @role, @status, @technicianId, @createdBy);";
        command.Parameters.AddWithValue("@id", account.Id);
        command.Parameters.AddWithValue("@fullName", account.FullName);
        command.Parameters.AddWithValue("@username", account.Username);
        command.Parameters.AddWithValue("@usernameNormalized", account.Username.ToUpperInvariant());
        command.Parameters.AddWithValue("@email", account.Email);
        command.Parameters.AddWithValue("@emailNormalized", account.Email.ToUpperInvariant());
        command.Parameters.AddWithValue("@passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("@role", account.Role);
        command.Parameters.AddWithValue("@status", account.Status);
        command.Parameters.AddWithValue("@technicianId", (object?)account.TechnicianId ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdBy", (object?)account.CreatedBy ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public Task<IReadOnlyList<StaffAccount>> GetAllActiveAsync() => GetAllAsync("ACTIVE");

    public async Task<IReadOnlyList<StaffAccount>> GetAllAsync(string? status = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(status))
        {
            command.CommandText = $"SELECT {SelectColumns} FROM staff_accounts ORDER BY created_at DESC;";
        }
        else
        {
            command.CommandText = $"SELECT {SelectColumns} FROM staff_accounts WHERE status = @status ORDER BY created_at DESC;";
            command.Parameters.AddWithValue("@status", status.Trim().ToUpperInvariant());
        }
        await using var reader = await command.ExecuteReaderAsync();
        var accounts = new List<StaffAccount>();
        while (await reader.ReadAsync()) accounts.Add(Map(reader));
        return accounts;
    }

    public async Task<StaffAccount?> FindByTechnicianIdAsync(string technicianId)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT username FROM staff_accounts WHERE technician_id = @technicianId LIMIT 1;";
        command.Parameters.AddWithValue("@technicianId", technicianId);
        var username = await command.ExecuteScalarAsync() as string;
        return username is null ? null : await FindByIdentifierAsync(username);
    }

    public async Task<bool> LinkTechnicianAsync(string accountId, string technicianId)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE staff_accounts SET technician_id = @technicianId
            WHERE id = @accountId AND role = 'Technician' AND status = 'ACTIVE'
              AND (technician_id IS NULL OR technician_id = @technicianId);";
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@technicianId", technicianId);
        return await command.ExecuteNonQueryAsync() > 0;
    }
}
