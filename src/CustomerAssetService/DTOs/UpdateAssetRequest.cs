using System.ComponentModel.DataAnnotations;

namespace CustomerAssetService.DTOs;

// The editable fields, and only those. Id comes from the route, and CustomerId,
// SerialNormalized and CreatedAt are server-controlled - an asset does not
// change hands, so the owner is not something an edit may rewrite. Kept as its
// own type rather than reusing CreateAssetRequest so the two request shapes can
// diverge without one dragging the other along.
public class UpdateAssetRequest
{
    /// <summary>Kind of asset. Required, and must be one of AIR_CONDITIONER, REFRIGERATOR, WASHING_MACHINE, WATER_HEATER or OTHER.</summary>
    [Required]
    [MaxLength(20)]
    [RegularExpression(
        "^(AIR_CONDITIONER|REFRIGERATOR|WASHING_MACHINE|WATER_HEATER|OTHER)$",
        ErrorMessage = "AssetType must be AIR_CONDITIONER, REFRIGERATOR, WASHING_MACHINE, WATER_HEATER or OTHER.")]
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Manufacturer's model designation. Required, up to 100 characters.</summary>
    [Required]
    [MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Serial number, stored as typed. Required, up to 100 characters. No two assets
    /// may hold the same serial once case and punctuation are stripped.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Date the asset was installed. Required, and a date only - no time of day.</summary>
    // Nullable so that "not supplied" is expressible. On a non-nullable DateOnly
    // the [Required] below can never fail - Required only rejects null - so an
    // omitted date bound silently to 0001-01-01 and was written as such.
    [Required]
    public DateOnly? InstallationDate { get; set; }

    /// <summary>Where the asset sits on the customer's premises. Required, up to 255 characters.</summary>
    [Required]
    [MaxLength(255)]
    public string Location { get; set; } = string.Empty;

    /// <summary>Optional free-text notes about the asset, up to 1000 characters.</summary>
    [MaxLength(1000)]
    public string? Notes { get; set; }
}
