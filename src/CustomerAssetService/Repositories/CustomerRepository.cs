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

    public async Task<List<Customer>> GetAllAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // The ORDER BY is not decoration: without one MySQL guarantees nothing
        // about row order, so the same query can come back shuffled between
        // requests. Newest first is what an Agent wants after registering someone.
        // created_at is a second-precision TIMESTAMP, so two customers registered
        // within the same second would tie and shuffle anyway - id breaks the tie
        // and makes the order total.
        command.CommandText = @"
            SELECT id, name, phone, phone_normalized, phone_active_unique, address,
                   customer_type, email, status, created_at, updated_at
            FROM customers
            ORDER BY created_at DESC, id;";

        await using var reader = await command.ExecuteReaderAsync();

        // Looked up by name so that reordering the SELECT list cannot silently
        // shift the mapping, and once up front rather than once per row.
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

        var customers = new List<Customer>();

        while (await reader.ReadAsync())
        {
            customers.Add(new Customer
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
            });
        }

        return customers;
    }

    // status is not in the SET list: activating or deactivating a customer is a
    // separate operation. created_at never changes, and updated_at is maintained
    // by the column's ON UPDATE default.
    public async Task UpdateAsync(Customer customer)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE customers
            SET name = @name,
                phone = @phone,
                phone_normalized = @phoneNormalized,
                address = @address,
                customer_type = @customerType,
                email = @email
            WHERE id = @id;";

        command.Parameters.AddWithValue("@name", customer.Name);
        command.Parameters.AddWithValue("@phone", customer.Phone);
        command.Parameters.AddWithValue("@phoneNormalized", customer.PhoneNormalized);
        command.Parameters.AddWithValue("@address", customer.Address);
        command.Parameters.AddWithValue("@customerType", customer.CustomerType);
        command.Parameters.AddWithValue("@email", (object?)customer.Email ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", customer.Id);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        // The exclusion clause is only in the SQL when there is an id to exclude,
        // so the create path runs exactly the query it ran before.
        var sql = @"
            SELECT 1
            FROM customers
            WHERE phone_normalized = @phoneNormalized AND status = 'ACTIVE'";

        if (excludeCustomerId is not null)
        {
            sql += " AND id != @excludeId";
        }

        command.CommandText = sql + @"
            LIMIT 1;";

        command.Parameters.AddWithValue("@phoneNormalized", phoneNormalized);

        if (excludeCustomerId is not null)
        {
            command.Parameters.AddWithValue("@excludeId", excludeCustomerId);
        }

        var result = await command.ExecuteScalarAsync();

        return result != null && result != DBNull.Value;
    }
}
