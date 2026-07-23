using HealthCare.Web.Auth;
using HealthCare.Web.Components;
using HealthCare.Web.Configuration;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<IApiTokenStore, ProtectedSessionApiTokenStore>();
builder.Services.AddScoped<IPermissionState, PermissionState>();
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
builder.Services.AddScoped<IAppointmentApiClient, AppointmentApiClient>();
builder.Services.AddScoped<IStaffPatientApiClient, StaffPatientApiClient>();

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
