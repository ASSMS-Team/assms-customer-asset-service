using CustomerAssetService.Models;

namespace CustomerAssetService.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CustomerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // phone_active_unique is a generated column and created_at / updated_at have
    // database defaults, so none of the three are written here.
    public async Task CreateAsync(Customer customer)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO customers
                (id, name, phone, phone_normalized, address, customer_type, email, status)
            VALUES
                (@id, @name, @phone, @phoneNormalized, @address, @customerType, @email, @status);";

        command.Parameters.AddWithValue("@id", customer.Id);
        command.Parameters.AddWithValue("@name", customer.Name);
        command.Parameters.AddWithValue("@phone", customer.Phone);
        command.Parameters.AddWithValue("@phoneNormalized", customer.PhoneNormalized);
        command.Parameters.AddWithValue("@address", customer.Address);
        command.Parameters.AddWithValue("@customerType", customer.CustomerType);
        command.Parameters.AddWithValue("@email", (object?)customer.Email ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", customer.Status);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<Customer?> GetByIdAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, name, phone, phone_normalized, phone_active_unique, address,
                   customer_type, email, status, created_at, updated_at
            FROM customers
            WHERE id = @id;";

        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        // Ordinals are looked up by name so that reordering the SELECT list
        // cannot silently shift the mapping.
        var idOrdinal = reader.GetOrdinal("id");
        var nameOrdinal = reader.GetOrdinal("name");
        var phoneOrdinal = reader.GetOrdinal("phone");
        var phoneNormalizedOrdinal = reader.GetOrdinal("phone_normalized");
        var phoneActiveUniqueOrdinal = reader.GetOrdinal("phone_active_unique");
        var addressOrdinal = reader.GetOrdinal("address");
        var customerTypeOrdinal = reader.GetOrdinal("customer_type");
        var emailOrdinal = reader.GetOrdinal("email");
        var statusOrdinal = reader.GetOrdinal("status");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");

        return new Customer
        {
            // MySqlConnector reads CHAR(36) as a Guid by default (GuidFormat=Char36),
            // so GetString throws on this column - go through the boxed value instead.
            Id = reader.GetValue(idOrdinal)?.ToString() ?? string.Empty,
            Name = reader.GetString(nameOrdinal),
            Phone = reader.GetString(phoneOrdinal),
            PhoneNormalized = reader.GetString(phoneNormalizedOrdinal),
            // NULL for every customer that is not ACTIVE.
            PhoneActiveUnique = reader.IsDBNull(phoneActiveUniqueOrdinal)
                ? null
                : reader.GetString(phoneActiveUniqueOrdinal),
            Address = reader.GetString(addressOrdinal),
            CustomerType = reader.GetString(customerTypeOrdinal),
            Email = reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal),
            Status = reader.GetString(statusOrdinal),
            CreatedAt = reader.GetDateTime(createdAtOrdinal),
            UpdatedAt = reader.GetDateTime(updatedAtOrdinal)
        };
    }

    public async Task<bool> ActivePhoneExistsAsync(string phoneNormalized)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 1
            FROM customers
            WHERE phone_normalized = @phoneNormalized AND status = 'ACTIVE'
            LIMIT 1;";

        command.Parameters.AddWithValue("@phoneNormalized", phoneNormalized);

        var result = await command.ExecuteScalarAsync();

        return result != null && result != DBNull.Value;
    }
}
