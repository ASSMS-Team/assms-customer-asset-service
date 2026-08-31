namespace CustomerAssetService.Models;

// Internal representation of a row in the assets table - all twelve columns,
// including the ones the API never exposes. Kept separate from the DTOs so the
// API shape can change without touching persistence.
public class Asset
{
    public string Id { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string AssetType { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string SerialNormalized { get; set; } = string.Empty;

    // installation_date is a DATE, not a TIMESTAMP - the day a unit was fitted
    // has no time of day, and DateOnly keeps it from acquiring a spurious one.
    public DateOnly InstallationDate { get; set; }

    public string Location { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
