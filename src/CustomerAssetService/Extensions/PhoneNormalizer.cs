namespace CustomerAssetService.Extensions;

// Canonical form for phone numbers: digits only, local shape, leading zero.
// +94 77 111 2222, 94771112222, 771112222 and 077-111-2222 all normalize to
// 0771112222, so the same person cannot be entered twice in different notations.
public static class PhoneNormalizer
{
    private const string SriLankaCountryCode = "94";
    private const int SubscriberDigits = 9;

    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // 94771112222 (or +94771112222, the + is already gone) -> 0771112222
        if (digits.Length == SriLankaCountryCode.Length + SubscriberDigits
            && digits.StartsWith(SriLankaCountryCode, StringComparison.Ordinal))
        {
            return "0" + digits.Substring(SriLankaCountryCode.Length);
        }

        // 771112222 -> 0771112222
        if (digits.Length == SubscriberDigits)
        {
            return "0" + digits;
        }

        // Already local (0771112222), or a shape we do not recognise - returned
        // as digits so it still compares consistently against itself.
        return digits;
    }
}
