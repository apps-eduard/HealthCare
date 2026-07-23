using System.Security.Cryptography;
using System.Text;
using HealthCare.Application.Identity;

namespace HealthCare.Infrastructure.Identity;

public sealed class RefreshTokenHasher : IRefreshTokenHasher
{
    public string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
