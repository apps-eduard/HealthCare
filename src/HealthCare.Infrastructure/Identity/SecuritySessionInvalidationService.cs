using HealthCare.Application.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Identity;

public sealed class SecuritySessionInvalidationService : ISecuritySessionInvalidationService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SecuritySessionInvalidationService> _logger;

    public SecuritySessionInvalidationService(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<SecuritySessionInvalidationService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<int> InvalidateUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var tokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = utcNow;
            token.RevokedReason = reason;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            await _userManager.UpdateSecurityStampAsync(user);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Security sessions invalidated. UserId={UserId} RevokedRefreshTokenCount={Count} Reason={Reason}",
            userId,
            tokens.Count,
            reason);

        return tokens.Count;
    }
}
