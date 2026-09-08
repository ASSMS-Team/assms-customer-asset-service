using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public sealed class StaffAccountRepository : IStaffAccountRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public StaffAccountRepository(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task<StaffAccount?> FindByIdentifierAsync(string identifier)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, username, email, password_hash, role, status
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
            Status = reader.GetString(reader.GetOrdinal("status"))
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
                (id, username, username_normalized, email, email_normalized, password_hash, role, status)
            VALUES
                (@id, @username, @usernameNormalized, @email, @emailNormalized, @passwordHash, @role, @status);";
        command.Parameters.AddWithValue("@id", account.Id);
        command.Parameters.AddWithValue("@username", account.Username);
        command.Parameters.AddWithValue("@usernameNormalized", account.Username.ToUpperInvariant());
        command.Parameters.AddWithValue("@email", account.Email);
        command.Parameters.AddWithValue("@emailNormalized", account.Email.ToUpperInvariant());
        command.Parameters.AddWithValue("@passwordHash", account.PasswordHash);
        command.Parameters.AddWithValue("@role", account.Role);
        command.Parameters.AddWithValue("@status", account.Status);
        await command.ExecuteNonQueryAsync();
    }
}
