using FluentAssertions;
using HealthCare.Infrastructure.Identity;

namespace HealthCare.UnitTests;

public sealed class RefreshTokenCryptoTests
{
    [Fact]
    public void GenerateRawToken_Is_Cryptographically_Random_And_Unique()
    {
        var generator = new RefreshTokenGenerator();

        var a = generator.GenerateRawToken();
        var b = generator.GenerateRawToken();

        a.Should().NotBeNullOrWhiteSpace();
        b.Should().NotBeNullOrWhiteSpace();
        a.Should().NotBe(b);
        Convert.FromBase64String(a).Length.Should().Be(64);
    }

    [Fact]
    public void Hash_Is_Deterministic_And_Does_Not_Equal_Raw_Token()
    {
        var hasher = new RefreshTokenHasher();
        const string raw = "sample-refresh-token-value";

        var hash1 = hasher.Hash(raw);
        var hash2 = hasher.Hash(raw);

        hash1.Should().Be(hash2);
        hash1.Should().NotBe(raw);
        hash1.Length.Should().Be(64); // SHA-256 hex
    }
}
