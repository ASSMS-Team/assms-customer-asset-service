using CustomerAssetService.Security;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotContainPlaintext_AndValidPasswordVerifies()
    {
        const string password = "Correct-Horse-Battery-76!";
        var hash = _hasher.Hash(password);

        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
        Assert.True(_hasher.Verify(password, hash));
        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-supported-hash")]
    [InlineData("pbkdf2-sha256$invalid$salt$hash")]
    [InlineData("pbkdf2-sha256$210000$$")]
    public void Verify_MalformedHash_ReturnsFalse(string hash)
    {
        Assert.False(_hasher.Verify("password", hash));
    }
}
