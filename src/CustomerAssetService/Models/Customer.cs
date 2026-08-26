namespace CustomerAssetService.Models;

// Internal representation of a row in the customers table - all eleven columns,
// including the ones the API never exposes. Kept separate from the DTOs so the
// API shape can change without touching persistence.
public class Customer
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string PhoneNormalized { get; set; } = string.Empty;

    // Generated column: phone_normalized while status is ACTIVE, otherwise NULL.
    // Database-computed and read-only - never written on insert or update.
    public string? PhoneActiveUnique { get; set; }

    public string Address { get; set; } = string.Empty;

    public string CustomerType { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
