using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using HealthCare.Application.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace HealthCare.UnitTests;

public sealed class AccessTokenServiceTests
{
    private static readonly string SigningKey = "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+";

    [Fact]
    public void CreateAccessToken_Includes_Required_Claims()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(time, accessMinutes: 15);
        var user = new ApplicationUser
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "doctor@example.com",
            UserName = "doctor@example.com",
        };

        var orgId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var clinicId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var result = service.CreateAccessToken(new AccessTokenUserContext(
            user,
            [AppRoles.Doctor],
            orgId,
            clinicId,
            PatientId: null));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.AccessToken);

        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == AppRoles.Doctor);
        jwt.Claims.Should().Contain(c => c.Type == AuthClaimTypes.OrganizationId && c.Value == orgId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == AuthClaimTypes.ClinicId && c.Value == clinicId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == AuthClaimTypes.TokenType && c.Value == AuthClaimTypes.AccessTokenType);
        jwt.Claims.Should().NotContain(c => c.Type == AuthClaimTypes.PatientId);
    }

    [Fact]
    public void CreateAccessToken_Expires_According_To_Configured_Lifetime()
    {
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        var service = CreateService(time, accessMinutes: 15);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            UserName = "admin@example.com",
        };

        var result = service.CreateAccessToken(new AccessTokenUserContext(
            user,
            [AppRoles.PlatformAdmin],
            null,
            null,
            null));

        result.ExpiresAtUtc.Should().Be(now.AddMinutes(15));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        jwt.ValidTo.Should().BeCloseTo(now.AddMinutes(15).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    private static AccessTokenService CreateService(TimeProvider timeProvider, int accessMinutes)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "HealthCare",
            Audience = "HealthCare",
            SigningKey = SigningKey,
            AccessTokenLifetimeMinutes = accessMinutes,
            RefreshTokenLifetimeDays = 7,
        });

        return new AccessTokenService(options, timeProvider);
    }
}

internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
