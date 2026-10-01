using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public sealed class StaffAccountRepository : ITechnicianAccountRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffAccountRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<StaffAccount?> FindByIdentifierAsync(string identifier)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, username, email, password_hash, role, status, technician_id
            FROM staff_accounts
            WHERE username_normalized = @identifier OR email_normalized = @identifier
            LIMIT 1;";
        command.Parameters.AddWithValue("@identifier", identifier.Trim().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new StaffAccount
        {
            Id = reader.GetValue(reader.GetOrdinal("id"))?.ToString() ?? string.Empty,
            Username = reader.GetString(reader.GetOrdinal("username")),
            Email = reader.GetString(reader.GetOrdinal("email")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            // MySqlConnector exposes CHAR(36) UUID columns as Guid values.
            // Convert the underlying value as for the staff ID above, rather
            // than calling GetString (which throws for linked accounts).
            TechnicianId = reader.IsDBNull(reader.GetOrdinal("technician_id"))
                ? null : reader.GetValue(reader.GetOrdinal("technician_id")).ToString()
        };
    }

    public async Task<bool> AnyManagerAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM staff_accounts WHERE role = 'Manager');";
        return Convert.ToBoolean(await command.ExecuteScalarAsync());
    }

    public async Task CreateAsync(StaffAccount account)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO staff_accounts
                (id, username, username_normalized, email, email_normalized, password_hash, role, status, technician_id)
            VALUES
                (@id, @username, @usernameNormalized, @email, @emailNormalized, @passwordHash, @role, @status, @technicianId);";
        command.Parameters.AddWithValue("@id", account.Id);
        command.Parameters.AddWithValue("@username", account.Username);
        command.Parameters.AddWithValue("@usernameNormalized", account.Username.ToUpperInvariant());
        command.Parameters.AddWithValue("@email", account.Email);
        command.Parameters.AddWithValue("@emailNormalized", account.Email.ToUpperInvariant());
        command.Parameters.AddWithValue("@passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("@role", account.Role);
        command.Parameters.AddWithValue("@status", account.Status);
        command.Parameters.AddWithValue("@technicianId", (object?)account.TechnicianId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
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
