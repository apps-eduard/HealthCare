using System.Security.Claims;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Web.Endpoints;

/// <summary>
/// BFF authentication mutations are POST-only and antiforgery-protected.
/// Login establishes the session in a single POST (no GET establish) so login CSRF relies on antiforgery
/// bound to the initiating browser rather than a redirectable establish step.
/// </summary>
public static class BffAuthEndpoints
{
    public static IEndpointRouteBuilder MapBffAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/auth");

        // Manual antiforgery validation with safe redirects — disable automatic 400 so UX stays form-friendly.
        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        // Legacy establish/GET logout also short-circuited in Program middleware (before antiforgery).
        group.MapMethods(
                "/establish",
                ["GET", "HEAD", "POST", "PUT", "DELETE", "PATCH"],
                EstablishRejectedAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        group.MapGet("/logout", LogoutGetRejectedAsync)
            .AllowAnonymous()
            .DisableAntiforgery();

        return endpoints;
    }

    private static IResult EstablishRejectedAsync() =>
        Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

    private static IResult LogoutGetRejectedAsync() =>
        Results.StatusCode(StatusCodes.Status405MethodNotAllowed);

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        [FromForm] string? email,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        IBffAuthService bffAuth,
        IApiTokenSessionStore sessions,
        IStaffWebAuthCookie webAuthCookie,
        IAntiforgery antiforgery,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HealthCare.Web.BffAuth");

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            logger.LogInformation("BFF auth event. Event=antiforgery_rejected Operation=login");
            return Results.Redirect(BuildLoginErrorUrl(returnUrl, "antiforgery"));
        }

        // Session fixation: discard any prior auth cookie/session before issuing a new one.
        var priorSessionId = httpContext.User.FindFirstValue(BffClaimTypes.SessionId);
        if (!string.IsNullOrWhiteSpace(priorSessionId))
        {
            await sessions.RemoveAsync(priorSessionId, httpContext.RequestAborted);
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        DeleteLegacyLoginCookies(httpContext);

        BffLoginResult result;
        try
        {
            result = await bffAuth.LoginAsync(email ?? string.Empty, password ?? string.Empty, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "BFF auth event. Event=login_failed ReasonCode=sign_in_failed");
            return Results.Redirect(BuildLoginErrorUrl(returnUrl, "sign_in_failed"));
        }

        if (!result.Succeeded || result.User is null || result.Session is null)
        {
            return Results.Redirect(BuildLoginErrorUrl(returnUrl, MapError(result.ErrorCode)));
        }

        await webAuthCookie.SignInAsync(result.User, result.Session.SessionId, httpContext.RequestAborted);
        logger.LogInformation(
            "BFF auth event. Event=session_rotated UserId={UserId}",
            result.User.UserId);

        if (result.IsPatientOnly || !result.IsStaffUser)
        {
            return Results.Redirect("/forbidden");
        }

        return Results.Redirect(SafeReturnUrl.Resolve(returnUrl));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IBffAuthService bffAuth,
        IAntiforgery antiforgery,
        IPlatformTenantContext platformTenant,
        IClinicWorkingContext clinicWorking,
        IPermissionState permissions,
        IClinicDirectoryCache clinicCache,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HealthCare.Web.BffAuth");
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            // CSRF logout must not succeed without a valid antiforgery token.
            logger.LogInformation("BFF auth event. Event=antiforgery_rejected Operation=logout");
            return Results.Redirect("/login?error=antiforgery");
        }

        await CompleteLogoutAsync(httpContext, bffAuth, platformTenant, clinicWorking, permissions, clinicCache);
        return Results.Redirect("/login");
    }

    private static async Task CompleteLogoutAsync(
        HttpContext httpContext,
        IBffAuthService bffAuth,
        IPlatformTenantContext platformTenant,
        IClinicWorkingContext clinicWorking,
        IPermissionState permissions,
        IClinicDirectoryCache clinicCache)
    {
        var sessionId = httpContext.User.FindFirstValue(BffClaimTypes.SessionId);
        await bffAuth.LogoutAsync(sessionId, callRemoteLogout: true, httpContext.RequestAborted);
        await permissions.ClearAsync();
        platformTenant.Clear();
        clinicWorking.Clear();
        clinicCache.Clear();
        DeleteLegacyLoginCookies(httpContext);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static void DeleteLegacyLoginCookies(HttpContext httpContext)
    {
        var secure = httpContext.Request.IsHttps;
        foreach (var name in new[] { BffCookieNames.LegacyLoginTicket, BffCookieNames.LegacyLoginCorrelation })
        {
            httpContext.Response.Cookies.Delete(name, new CookieOptions
            {
                Path = "/bff/auth",
                Secure = secure,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
            });
            httpContext.Response.Cookies.Delete(name, new CookieOptions
            {
                Path = "/",
                Secure = secure,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
            });
        }
    }

    private static string BuildLoginErrorUrl(string? returnUrl, string errorCode)
    {
        var url = $"/login?error={Uri.EscapeDataString(errorCode)}";
        if (SafeReturnUrl.TryValidate(returnUrl, out var safe))
        {
            url += $"&returnUrl={Uri.EscapeDataString(safe)}";
        }

        return url;
    }

    private static string MapError(string? errorCode) =>
        errorCode switch
        {
            AuthErrorCodes.InvalidCredentials => "invalid_credentials",
            AuthErrorCodes.AccountDisabled => "account_disabled",
            AuthErrorCodes.AccountLocked => "account_locked",
            AuthErrorCodes.EmailNotConfirmed => "email_not_confirmed",
            AuthErrorCodes.OrganizationInactive => "organization_inactive",
            AuthErrorCodes.ClinicInactive => "clinic_inactive",
            "antiforgery" => "antiforgery",
            _ => "sign_in_failed",
        };
}
