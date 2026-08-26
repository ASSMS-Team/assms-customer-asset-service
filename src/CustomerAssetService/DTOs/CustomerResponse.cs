namespace CustomerAssetService.DTOs;

// What goes back to the client. PhoneNormalized and PhoneActiveUnique are
// excluded on purpose: they are internal mechanics with no business meaning.
// Phone is returned exactly as the Agent typed it.
public class CustomerResponse
{
    /// <summary>Server-generated customer id (a GUID string). Use it to fetch the customer.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Full name of the customer.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Contact phone number, returned exactly as it was submitted.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Postal address of the customer.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Kind of customer: INDIVIDUAL or BUSINESS.</summary>
    public string CustomerType { get; set; } = string.Empty;

    /// <summary>Email address, or null if none was supplied.</summary>
    public string? Email { get; set; }

    /// <summary>Lifecycle status of the customer. Newly created customers are ACTIVE.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Database timestamp for when the customer was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Database timestamp for when the customer was last modified.</summary>
    public DateTime UpdatedAt { get; set; }
}
