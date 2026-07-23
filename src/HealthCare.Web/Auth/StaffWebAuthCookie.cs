using System.Security.Claims;
using HealthCare.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HealthCare.Web.Auth;

/// <summary>
/// Issues/clears the staff Web auth cookie. Cookie holds minimal identity claims only — never API tokens.
/// </summary>
public interface IStaffWebAuthCookie
{
    Task SignInAsync(CurrentUserResponse user, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}

public sealed class StaffWebAuthCookie : IStaffWebAuthCookie
{
    public const string AuthenticationType = "HealthCareStaffCookie";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<StaffWebAuthCookie> _logger;

    public StaffWebAuthCookie(
        IHttpContextAccessor httpContextAccessor,
        ILogger<StaffWebAuthCookie> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task SignInAsync(CurrentUserResponse user, CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("Cannot issue staff auth cookie: HttpContext is unavailable.");
            return;
        }

        var principal = CreatePrincipal(user);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
        _logger.LogInformation("Staff web auth cookie issued for user {UserId}.", user.UserId);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        try
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("Staff web auth cookie cleared.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear staff web auth cookie.");
        }
    }

    public static ClaimsPrincipal CreatePrincipal(CurrentUserResponse user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString("D")),
            new(ClaimTypes.Name, user.Email ?? user.UserId.ToString("D")),
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Intentionally omit API access/refresh tokens and permission catalog from the cookie.
        var identity = new ClaimsIdentity(claims, AuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}
