using System.Security.Claims;
using HealthCare.Web.Auth;
using HealthCare.Web.Components;
using HealthCare.Web.Configuration;
using HealthCare.Web.Endpoints;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<BffOptions>(builder.Configuration.GetSection(BffOptions.SectionName));

var bffOptions = builder.Configuration.GetSection(BffOptions.SectionName).Get<BffOptions>() ?? new BffOptions();
ValidateBffCookieConfiguration(builder.Environment, bffOptions);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();
builder.Services.AddDataProtection()
    .SetApplicationName("HealthCare.Web");

// Development: in-memory distributed cache. Production must use a shared cache (Redis/SQL) for multi-instance.
builder.Services.AddDistributedMemoryCache();

var cookieName = ResolveAuthCookieName(builder.Environment, bffOptions);
var requireSecureCookie = !builder.Environment.IsDevelopment() || bffOptions.RequireHttps;

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = requireSecureCookie
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.Cookie.Path = "/";
        // __Host- cookies must not set Domain.
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/forbidden";
        options.ReturnUrlParameter = "returnUrl";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Max(1, bffOptions.AbsoluteSessionHours));
        options.Events.OnRedirectToLogin = context =>
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Response.Redirect(SafeReturnUrl.BuildLoginUrl(returnUrl));
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect("/forbidden");
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var sid = context.Principal?.FindFirst(BffClaimTypes.SessionId)?.Value;
            var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(sid)
                || !Guid.TryParse(userIdClaim, out var cookieUserId)
                || cookieUserId == Guid.Empty)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var store = context.HttpContext.RequestServices.GetRequiredService<IApiTokenSessionStore>();
            var session = await store.GetAsync(sid, context.HttpContext.RequestAborted);
            if (session is null || session.UserId != cookieUserId)
            {
                if (session is not null)
                {
                    await store.RemoveAsync(sid, context.HttpContext.RequestAborted);
                }

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HealthCare.Web.BffAuth");
                logger.LogInformation("BFF auth event. Event=session_mismatch");

                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IApiTokenSessionStore, DistributedCacheApiTokenSessionStore>();
builder.Services.AddScoped<IApiTokenStore, ServerSessionApiTokenStore>();
builder.Services.AddScoped<IBffAuthService, BffAuthService>();
builder.Services.AddScoped<IPermissionState, PermissionState>();
builder.Services.AddScoped<IPlatformTenantContext, PlatformTenantContext>();
builder.Services.AddScoped<IStaffWebAuthCookie, StaffWebAuthCookie>();
builder.Services.AddScoped<StaffAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<StaffAuthenticationStateProvider>());

builder.Services.AddTransient<AuthDelegatingHandler>();
builder.Services.AddHttpClient("HealthCareApi.Anonymous", ConfigureApiClient);
builder.Services.AddHttpClient("HealthCareApi", ConfigureApiClient)
    .AddHttpMessageHandler<AuthDelegatingHandler>();

builder.Services.AddScoped<IAuthApiClient, AuthApiClient>();
builder.Services.AddScoped<IStaffManagementApiClient, StaffManagementApiClient>();
builder.Services.AddScoped<IClinicDirectoryApiClient, ClinicDirectoryApiClient>();
builder.Services.AddScoped<IClinicDirectoryCache, ClinicDirectoryCache>();
builder.Services.AddScoped<IOrganizationDirectoryApiClient, OrganizationDirectoryApiClient>();
builder.Services.AddScoped<IAppointmentApiClient, AppointmentApiClient>();
builder.Services.AddScoped<IStaffPatientApiClient, StaffPatientApiClient>();
builder.Services.AddScoped<IDoctorAvailabilityApiClient, DoctorAvailabilityApiClient>();

builder.Services.AddHttpClient();
builder.Services.AddAntDesign();
builder.Services.AddScoped<IUserNotificationService, AntUserNotificationService>();
builder.Services.AddScoped<IUiModalService, AntUiModalService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Reject legacy state-changing auth routes before antiforgery runs (avoids misleading AF 400).
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.Equals("/bff/auth/establish", StringComparison.OrdinalIgnoreCase)
        || (HttpMethods.IsGet(context.Request.Method)
            && path.Equals("/bff/auth/logout", StringComparison.OrdinalIgnoreCase)))
    {
        // Prevent StatusCodePagesWithReExecute from turning 405 into a POST /not-found antiforgery 400.
        var statusCodePages = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodePages is not null)
        {
            statusCodePages.Enabled = false;
        }

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }

    await next();
});

app.UseAntiforgery();

app.MapBffAuthEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveAuthCookieName(IHostEnvironment env, BffOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.CookieName))
    {
        return options.CookieName;
    }

    return env.IsDevelopment() && !options.RequireHttps
        ? BffCookieNames.AuthDevelopment
        : BffCookieNames.AuthProduction;
}

static void ValidateBffCookieConfiguration(IHostEnvironment env, BffOptions options)
{
    var name = ResolveAuthCookieName(env, options);
    var requireSecure = !env.IsDevelopment() || options.RequireHttps;
    if (name.StartsWith("__Host-", StringComparison.Ordinal) && !requireSecure)
    {
        throw new InvalidOperationException(
            "Bff cookie name uses the __Host- prefix, which requires Secure cookies. Set Bff:RequireHttps=true or use HealthCare.Staff.Auth in Development.");
    }
}

static void ConfigureApiClient(IServiceProvider services, HttpClient client)
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
        ? "http://localhost:5080/"
        : options.BaseUrl;
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
}

public partial class Program;
