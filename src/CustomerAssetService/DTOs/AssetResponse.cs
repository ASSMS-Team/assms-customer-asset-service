namespace CustomerAssetService.DTOs;

// What goes back to the client. SerialNormalized is excluded on purpose: it is
// internal mechanics with no business meaning. SerialNumber is returned exactly
// as the Agent typed it.
public class AssetResponse
{
    /// <summary>Server-generated asset id (a GUID string). Use it to fetch the asset.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Id of the customer that owns this asset.</summary>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Kind of asset: AIR_CONDITIONER, REFRIGERATOR, WASHING_MACHINE, WATER_HEATER or OTHER.</summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Manufacturer's model designation.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Serial number, returned exactly as it was submitted.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Date the asset was installed.</summary>
    public DateOnly InstallationDate { get; set; }

    /// <summary>Where the asset sits on the customer's premises.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Free-text notes about the asset, or null if none were supplied.</summary>
    public string? Notes { get; set; }

    /// <summary>Database timestamp for when the asset was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Database timestamp for when the asset was last modified.</summary>
    public DateTime UpdatedAt { get; set; }
}
