using System.Security.Claims;
using HealthCare.Domain.Identity;

namespace HealthCare.Application.Identity;

public sealed record AccessTokenUserContext(
    ApplicationUser User,
    IReadOnlyList<string> Roles,
    Guid? OrganizationId,
    Guid? ClinicId,
    Guid? PatientId);

public sealed record AccessTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<Claim> Claims);

public interface IAccessTokenService
{
    AccessTokenResult CreateAccessToken(AccessTokenUserContext context);
}
