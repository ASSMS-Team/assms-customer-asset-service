using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

// What the client is allowed to send. Id, Status and PhoneNormalized are
// deliberately absent - they are server-controlled, and binding them from the
// request body would be a mass-assignment hole.
public class CreateCustomerRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Address { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    [RegularExpression("^(INDIVIDUAL|BUSINESS)$", ErrorMessage = "CustomerType must be INDIVIDUAL or BUSINESS.")]
    public string CustomerType { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }
}
