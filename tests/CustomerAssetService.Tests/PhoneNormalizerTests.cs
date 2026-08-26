using CustomerAssetService.Extensions;

namespace CustomerAssetService.Tests;

public class PhoneNormalizerTests
{
    [Theory]
    // Already local.
    [InlineData("0771112222", "0771112222")]
    // Punctuation and spacing stripped.
    [InlineData("077-111-2222", "0771112222")]
    [InlineData("077 111 2222", "0771112222")]
    [InlineData("(077) 111-2222", "0771112222")]
    // International, with and without the plus.
    [InlineData("+94771112222", "0771112222")]
    [InlineData("+94 77 111 2222", "0771112222")]
    [InlineData("94771112222", "0771112222")]
    // Subscriber digits only, no leading zero.
    [InlineData("771112222", "0771112222")]
    public void Normalize_ReturnsCanonicalLocalForm(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmpty_ForMissingInput(string? input)
    {
        Assert.Equal(string.Empty, PhoneNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_MapsEveryVariantOfTheSameNumberToOneValue()
    {
        var variants = new[] { "0771112222", "077-111-2222", "+94771112222", "94771112222", "771112222" };

        var normalized = variants.Select(PhoneNormalizer.Normalize).Distinct().ToList();

        Assert.Single(normalized);
        Assert.Equal("0771112222", normalized[0]);
    }

    [Fact]
    public void Normalize_KeepsDigits_ForUnrecognisedShapes()
    {
        Assert.Equal("12345", PhoneNormalizer.Normalize("1-2-3-4-5"));
    }
}
