using CustomerAssetService.Extensions;

namespace CustomerAssetService.Tests;

public class SerialNormalizerTests
{
    [Theory]
    // The three notations the same serial is typed in.
    [InlineData("ABC-123", "ABC123")]
    [InlineData("abc 123", "ABC123")]
    [InlineData("Abc123", "ABC123")]
    // Already canonical.
    [InlineData("ABC123", "ABC123")]
    // Other separators manufacturers print.
    [InlineData("ABC/123", "ABC123")]
    [InlineData("ABC_123", "ABC123")]
    [InlineData("ABC.123", "ABC123")]
    // Surrounding whitespace, from a paste.
    [InlineData("  abc-123  ", "ABC123")]
    // Digits only, and letters only.
    [InlineData("123-456", "123456")]
    [InlineData("sn-abc", "SNABC")]
    public void Normalize_ReturnsCanonicalUppercaseAlphanumericForm(string input, string expected)
    {
        Assert.Equal(expected, SerialNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmpty_ForMissingInput(string? input)
    {
        Assert.Equal(string.Empty, SerialNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_ReturnsEmpty_WhenNothingAlphanumericSurvives()
    {
        Assert.Equal(string.Empty, SerialNormalizer.Normalize("---"));
    }

    [Fact]
    public void Normalize_MapsEveryVariantOfTheSameSerialToOneValue()
    {
        var variants = new[] { "ABC-123", "abc 123", "Abc123", "ABC123", "a-B-c-1-2-3" };

        var normalized = variants.Select(SerialNormalizer.Normalize).Distinct().ToList();

        Assert.Single(normalized);
        Assert.Equal("ABC123", normalized[0]);
    }
}
