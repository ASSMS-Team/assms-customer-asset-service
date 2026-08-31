using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

// What the client is allowed to send. Id, Status and PhoneNormalized are
// deliberately absent - they are server-controlled, and binding them from the
// request body would be a mass-assignment hole.
public class CreateCustomerRequest
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
