namespace CustomerAssetService.Extensions;

// Canonical form for serial numbers: uppercase, alphanumerics only.
// ABC-123, abc 123 and Abc123 all normalize to ABC123, so the same physical
// unit cannot be registered twice in different notations.
public static class SerialNormalizer
{
    public static string Normalize(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return string.Empty;
        }

        // Manufacturers print serials with hyphens, spaces and slashes that
        // carry no meaning, and an Agent copying one off a unit will not
        // reproduce them consistently.
        var alphanumerics = serial.Where(char.IsLetterOrDigit).ToArray();

        return new string(alphanumerics).ToUpperInvariant();
    }
}
