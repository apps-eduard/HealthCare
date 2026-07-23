using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HealthCare.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HealthCare.Infrastructure.Identity;

public sealed class AccessTokenService : IAccessTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public AccessTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        EnsureSigningKeyConfigured(_options.SigningKey);
    }

    public AccessTokenResult CreateAccessToken(AccessTokenUserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.User);

        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, context.User.Id.ToString()),
            new(ClaimTypes.NameIdentifier, context.User.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AuthClaimTypes.TokenType, AuthClaimTypes.AccessTokenType),
        };

        if (!string.IsNullOrWhiteSpace(context.User.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, context.User.Email));
            claims.Add(new Claim(ClaimTypes.Email, context.User.Email));
        }

        foreach (var role in context.Roles.Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (context.OrganizationId is Guid organizationId)
        {
            claims.Add(new Claim(AuthClaimTypes.OrganizationId, organizationId.ToString()));
        }

        if (context.ClinicId is Guid clinicId)
        {
            claims.Add(new Claim(AuthClaimTypes.ClinicId, clinicId.ToString()));
        }

        if (context.PatientId is Guid patientId)
        {
            claims.Add(new Claim(AuthClaimTypes.PatientId, patientId.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessTokenResult(encoded, expires, claims);
    }

    internal static void EnsureSigningKeyConfigured(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey)
            || signingKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)
            || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with a secret of at least 32 UTF-8 bytes. " +
                "Use user secrets or environment variables. Do not use production secrets in source control.");
        }
    }
}
