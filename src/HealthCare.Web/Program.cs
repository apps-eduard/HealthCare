using HealthCare.Web.Auth;
using HealthCare.Web.Components;
using HealthCare.Web.Configuration;
using HealthCare.Web.Endpoints;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<BffOptions>(builder.Configuration.GetSection(BffOptions.SectionName));

var bffOptions = builder.Configuration.GetSection(BffOptions.SectionName).Get<BffOptions>() ?? new BffOptions();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery();
builder.Services.AddDataProtection()
    .SetApplicationName("HealthCare.Web");

// Development: in-memory distributed cache. Production must use a shared cache (Redis/SQL) for multi-instance.
builder.Services.AddDistributedMemoryCache();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = string.IsNullOrWhiteSpace(bffOptions.CookieName)
            ? "HealthCare.Staff.Auth"
            : bffOptions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() && !bffOptions.RequireHttps
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.Path = "/";
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
            if (string.IsNullOrWhiteSpace(sid))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var store = context.HttpContext.RequestServices.GetRequiredService<IApiTokenSessionStore>();
            if (!await store.ExistsAsync(sid, context.HttpContext.RequestAborted))
            {
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

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
});

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
app.UseAntiforgery();

app.MapBffAuthEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

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

// Expose Program for WebApplicationFactory-style tests if added later.
public partial class Program;
