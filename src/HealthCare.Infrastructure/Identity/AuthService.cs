using HealthCare.Application.Identity;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly HealthCareDbContext _dbContext;
    private readonly IAccessTokenService _accessTokenService;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly IRefreshTokenHasher _refreshTokenHasher;
    private readonly JwtOptions _jwtOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        HealthCareDbContext dbContext,
        IAccessTokenService accessTokenService,
        IRefreshTokenGenerator refreshTokenGenerator,
        IRefreshTokenHasher refreshTokenHasher,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _accessTokenService = accessTokenService;
        _refreshTokenGenerator = refreshTokenGenerator;
        _refreshTokenHasher = refreshTokenHasher;
        _jwtOptions = jwtOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthTokenResponse> LoginAsync(
        LoginRequest request,
        AuthClientContext client,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = _userManager.NormalizeEmail(request.Email);
        var user = await _userManager.Users
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            _logger.LogInformation("Login failed for unknown account");
            throw AuthenticationException.InvalidCredentials();
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            throw AuthenticationException.AccountLocked();
        }

        if (signInResult.IsNotAllowed)
        {
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                _logger.LogInformation("Login blocked: email not confirmed. UserId={UserId}", user.Id);
                throw AuthenticationException.EmailNotConfirmed();
            }

            _logger.LogInformation("Login not allowed for user {UserId}", user.Id);
            throw AuthenticationException.InvalidCredentials();
        }

        if (!signInResult.Succeeded)
        {
            // Do not reveal whether the account exists or which factor failed.
            _logger.LogInformation("Login failed for user {UserId}", user.Id);
            throw AuthenticationException.InvalidCredentials();
        }

        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            _logger.LogInformation("Login blocked: email not confirmed. UserId={UserId}", user.Id);
            throw AuthenticationException.EmailNotConfirmed();
        }

        if (!user.IsActive)
        {
            throw AuthenticationException.AccountDisabled();
        }

        var staff = await LoadActiveStaffContextAsync(user.Id, cancellationToken);
        var roles = await _userManager.GetRolesAsync(user);
        var patientId = await _dbContext.Patients
            .AsNoTracking()
            .Where(p => p.UserId == user.Id && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return await IssueTokenPairAsync(
            user,
            roles,
            staff?.OrganizationId,
            staff?.ClinicId,
            patientId,
            familyId: Guid.NewGuid(),
            client,
            cancellationToken);
    }

    public async Task<AuthTokenResponse> RefreshAsync(
        RefreshTokenRequest request,
        AuthClientContext client,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = _refreshTokenHasher.Hash(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (existing is null)
        {
            throw AuthenticationException.InvalidRefreshToken();
        }

        var utcNow = _timeProvider.GetUtcNow();

        if (existing.IsRevoked)
        {
            // Reuse of a rotated token: revoke the entire family.
            if (existing.ReplacedByTokenId.HasValue)
            {
                await RevokeFamilyAsync(existing.FamilyId, "ReuseDetected", utcNow, cancellationToken);
                _logger.LogWarning(
                    "Refresh token reuse detected for family {FamilyId}; family revoked",
                    existing.FamilyId);
                throw AuthenticationException.ReusedRefreshToken();
            }

            throw AuthenticationException.RevokedRefreshToken();
        }

        if (existing.IsExpired(utcNow))
        {
            throw AuthenticationException.ExpiredRefreshToken();
        }

        var user = await _userManager.FindByIdAsync(existing.UserId.ToString())
            ?? throw AuthenticationException.InvalidRefreshToken();

        if (!user.IsActive)
        {
            await RevokeFamilyAsync(existing.FamilyId, "AccountDisabled", utcNow, cancellationToken);
            throw AuthenticationException.AccountDisabled();
        }

        var staff = await LoadActiveStaffContextAsync(user.Id, cancellationToken);
        var roles = await _userManager.GetRolesAsync(user);

        var rawRefresh = _refreshTokenGenerator.GenerateRawToken();
        var newRefresh = CreateRefreshTokenEntity(
            user.Id,
            existing.FamilyId,
            rawRefresh,
            client,
            utcNow);

        existing.RevokedAtUtc = utcNow;
        existing.RevokedReason = "Rotated";
        existing.ReplacedByTokenId = newRefresh.Id;

        _dbContext.RefreshTokens.Add(newRefresh);

        var access = _accessTokenService.CreateAccessToken(
            new AccessTokenUserContext(
                user,
                roles.ToList(),
                staff?.OrganizationId,
                staff?.ClinicId,
                PatientId: null));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse
        {
            AccessToken = access.AccessToken,
            RefreshToken = rawRefresh,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = newRefresh.ExpiresAtUtc,
            TokenType = "Bearer",
        };
    }

    public async Task LogoutAsync(
        LogoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = _refreshTokenHasher.Hash(request.RefreshToken);
        var existing = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (existing is null || existing.IsRevoked)
        {
            // Idempotent logout — do not reveal token validity.
            return;
        }

        var utcNow = _timeProvider.GetUtcNow();
        existing.RevokedAtUtc = utcNow;
        existing.RevokedReason = "Logout";
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthTokenResponse> IssueTokenPairAsync(
        ApplicationUser user,
        IList<string> roles,
        Guid? organizationId,
        Guid? clinicId,
        Guid? patientId,
        Guid familyId,
        AuthClientContext client,
        CancellationToken cancellationToken)
    {
        var utcNow = _timeProvider.GetUtcNow();
        var rawRefresh = _refreshTokenGenerator.GenerateRawToken();
        var refreshEntity = CreateRefreshTokenEntity(user.Id, familyId, rawRefresh, client, utcNow);

        _dbContext.RefreshTokens.Add(refreshEntity);

        var access = _accessTokenService.CreateAccessToken(
            new AccessTokenUserContext(user, roles.ToList(), organizationId, clinicId, patientId));

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse
        {
            AccessToken = access.AccessToken,
            RefreshToken = rawRefresh,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshTokenExpiresAtUtc = refreshEntity.ExpiresAtUtc,
            TokenType = "Bearer",
        };
    }

    private RefreshToken CreateRefreshTokenEntity(
        Guid userId,
        Guid familyId,
        string rawToken,
        AuthClientContext client,
        DateTimeOffset utcNow)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = _refreshTokenHasher.Hash(rawToken),
            CreatedAtUtc = utcNow,
            ExpiresAtUtc = utcNow.AddDays(_jwtOptions.RefreshTokenLifetimeDays),
            CreatedByIp = Truncate(client.IpAddress, 64),
            CreatedByUserAgent = Truncate(client.UserAgent, 512),
        };
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        string reason,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = utcNow;
            token.RevokedReason = reason;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<StaffMember?> LoadActiveStaffContextAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var staff = await _dbContext.StaffMembers
            .AsNoTracking()
            .Include(s => s.Organization)
            .Include(s => s.Clinic)
            .SingleOrDefaultAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

        if (staff is null)
        {
            return null;
        }

        if (staff.Organization is null
            || staff.Organization.Status != OrganizationStatus.Active)
        {
            throw AuthenticationException.OrganizationInactive();
        }

        if (staff.Clinic is null || !staff.Clinic.IsActive)
        {
            throw AuthenticationException.ClinicInactive();
        }

        return staff;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
