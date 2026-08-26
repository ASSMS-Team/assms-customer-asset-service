namespace CustomerAssetService.DTOs;

// What goes back to the client. PhoneNormalized and PhoneActiveUnique are
// excluded on purpose: they are internal mechanics with no business meaning.
// Phone is returned exactly as the Agent typed it.
public class CustomerResponse
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string CustomerType { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
