using HealthCare.Web.Auth;
using HealthCare.Web.Components;
using HealthCare.Web.Configuration;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "HealthCare.Staff.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/forbidden";
        options.ReturnUrlParameter = "returnUrl";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            // Staff Web is HTML-only; always challenge to login with a safe local return URL.
            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Response.Redirect(SafeReturnUrl.BuildLoginUrl(returnUrl));
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect("/forbidden");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IApiTokenStore, ProtectedSessionApiTokenStore>();
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
