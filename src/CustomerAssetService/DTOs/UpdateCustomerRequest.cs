using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

// The editable fields, and only those. Id comes from the route, and Status,
// PhoneNormalized, CreatedAt are server-controlled exactly as they are on
// create. Kept as its own type rather than reusing CreateCustomerRequest so the
// two request shapes can diverge without one dragging the other along.
public class UpdateCustomerRequest
{
    /// <summary>Full name of the customer. Required, up to 100 characters.</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Contact phone number, stored as typed. Required, up to 20 characters. Only one
    /// active customer may hold a given number once formatting is stripped.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Postal address of the customer. Required, up to 255 characters.</summary>
    [Required]
    [MaxLength(255)]
    public string Address { get; set; } = string.Empty;

    /// <summary>Kind of customer. Required, and must be either INDIVIDUAL or BUSINESS.</summary>
    [Required]
    [MaxLength(20)]
    [RegularExpression("^(INDIVIDUAL|BUSINESS)$", ErrorMessage = "CustomerType must be INDIVIDUAL or BUSINESS.")]
    public string CustomerType { get; set; } = string.Empty;

    /// <summary>Optional email address. When supplied it must be a valid address of up to 255 characters.</summary>
    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }
}
