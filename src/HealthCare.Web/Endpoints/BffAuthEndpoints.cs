using System.Net.Http.Json;
using System.Security.Claims;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Web.Endpoints;

public static class BffAuthEndpoints
{
    public static IEndpointRouteBuilder MapBffAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .DisableAntiforgery(); // form posts use explicit ValidateAntiforgery below

        group.MapGet("/establish", EstablishAsync)
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous();

        group.MapGet("/logout", LogoutGetAsync)
            .AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        [FromForm] string? email,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        IBffAuthService bffAuth,
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
            logger.LogInformation("BFF login rejected: invalid antiforgery token.");
            return Results.Redirect(BuildLoginErrorUrl(returnUrl, "antiforgery"));
        }

        // Session fixation: clear any prior cookie before establishing a new login ticket.
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var result = await bffAuth.LoginAsync(email ?? string.Empty, password ?? string.Empty, httpContext.RequestAborted);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.EstablishTicket))
        {
            return Results.Redirect(BuildLoginErrorUrl(returnUrl, MapError(result.ErrorCode)));
        }

        httpContext.Response.Cookies.Append(
            LoginTicketCookieName,
            result.EstablishTicket,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromSeconds(60),
                Path = "/bff/auth",
            });

        var establishUrl = "/bff/auth/establish"
            + $"?returnUrl={Uri.EscapeDataString(SafeReturnUrl.Resolve(returnUrl))}";
        if (result.IsPatientOnly || !result.IsStaffUser)
        {
            establishUrl += "&staff=0";
        }

        return Results.Redirect(establishUrl);
    }

    private const string LoginTicketCookieName = "HealthCare.Staff.LoginTicket";

    private static async Task<IResult> EstablishAsync(
        HttpContext httpContext,
        [FromQuery] string? returnUrl,
        [FromQuery] string? staff,
        IApiTokenSessionStore sessions,
        IHttpClientFactory httpClientFactory,
        IStaffWebAuthCookie webAuthCookie,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HealthCare.Web.BffAuth");

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        httpContext.Request.Cookies.TryGetValue(LoginTicketCookieName, out var ticket);
        httpContext.Response.Cookies.Delete(LoginTicketCookieName, new CookieOptions
        {
            Path = "/bff/auth",
            Secure = httpContext.Request.IsHttps,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        var consumed = await sessions.ConsumeLoginTicketAsync(ticket ?? string.Empty, httpContext.RequestAborted);
        if (consumed is null)
        {
            logger.LogInformation("BFF establish failed: missing or expired login ticket.");
            return Results.Redirect("/login?error=session");
        }

        var (sessionId, userId) = consumed.Value;
        var session = await sessions.GetAsync(sessionId, httpContext.RequestAborted);
        if (session is null || session.UserId != userId)
        {
            await sessions.RemoveAsync(sessionId, httpContext.RequestAborted);
            return Results.Redirect("/login?error=session");
        }

        var anonymous = httpClientFactory.CreateClient("HealthCareApi.Anonymous");
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
        meRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);
        using var meResponse = await anonymous.SendAsync(meRequest, httpContext.RequestAborted);
        if (!meResponse.IsSuccessStatusCode)
        {
            await sessions.RemoveAsync(sessionId, httpContext.RequestAborted);
            return Results.Redirect("/login?error=session");
        }

        var user = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(httpContext.RequestAborted);
        if (user is null || user.UserId != userId)
        {
            await sessions.RemoveAsync(sessionId, httpContext.RequestAborted);
            return Results.Redirect("/login?error=session");
        }

        // Bind HttpContext accessor for cookie helper.
        await webAuthCookie.SignInAsync(user, sessionId, httpContext.RequestAborted);

        if (string.Equals(staff, "0", StringComparison.Ordinal))
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
            logger.LogInformation("BFF logout antiforgery failed; continuing local sign-out.");
        }

        await CompleteLogoutAsync(httpContext, bffAuth, platformTenant, permissions, clinicCache);
        return Results.Redirect("/login");
    }

    private static async Task<IResult> LogoutGetAsync(
        HttpContext httpContext,
        IBffAuthService bffAuth,
        IPlatformTenantContext platformTenant,
        IPermissionState permissions,
        IClinicDirectoryCache clinicCache)
    {
        // GET logout is used after circuit-local cleanup + forceLoad. Session should already be cleared;
        // still clear cookie and any residual server session.
        await CompleteLogoutAsync(httpContext, bffAuth, platformTenant, permissions, clinicCache);
        return Results.Redirect("/login");
    }

    private static async Task CompleteLogoutAsync(
        HttpContext httpContext,
        IBffAuthService bffAuth,
        IPlatformTenantContext platformTenant,
        IPermissionState permissions,
        IClinicDirectoryCache clinicCache)
    {
        var sessionId = httpContext.User.FindFirstValue(BffClaimTypes.SessionId);
        await bffAuth.LogoutAsync(sessionId, callRemoteLogout: true, httpContext.RequestAborted);
        await permissions.ClearAsync();
        platformTenant.Clear();
        clinicCache.Clear();
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
