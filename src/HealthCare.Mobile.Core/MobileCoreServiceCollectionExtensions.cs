using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Appointments;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Booking;
using HealthCare.Mobile.Core.Configuration;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Core.Patients;
using HealthCare.Mobile.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace HealthCare.Mobile.Core;

public static class MobileCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHealthCareMobileCore(
        this IServiceCollection services,
        Action<MobileAppOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<MobileAppOptions>();
        }

        services.TryAddSingleton<IAuthSessionService, AuthSessionService>();
        services.TryAddSingleton<IDiscoveryStateService, DiscoveryStateService>();
        services.TryAddSingleton<IBookingReceiptStore, BookingReceiptStore>();
        services.TryAddSingleton<ITokenRefresher, TokenRefresher>();
        services.TryAddTransient<AuthenticatingHttpMessageHandler>();
        services.TryAddSingleton<IHealthCareApiClient, HealthCareApiClient>();
        services.TryAddSingleton<IPatientAuthenticationService, PatientAuthenticationService>();
        services.TryAddSingleton<IPatientProfileService, PatientProfileService>();
        services.TryAddSingleton<IPatientDiscoveryService, PatientDiscoveryService>();
        services.TryAddSingleton<IPatientBookingService, PatientBookingService>();
        services.TryAddSingleton<IPatientAppointmentService, PatientAppointmentService>();

        services.AddHttpClient(MobileHttpClientNames.Anonymous, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MobileAppOptions>>().Value;
            client.BaseAddress = MobileAppOptionsValidator.GetNormalizedBaseAddress(options);
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddHttpClient(MobileHttpClientNames.Authenticated, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MobileAppOptions>>().Value;
                client.BaseAddress = MobileAppOptionsValidator.GetNormalizedBaseAddress(options);
                client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
            })
            .AddHttpMessageHandler<AuthenticatingHttpMessageHandler>();

        return services;
    }

    public static IServiceCollection AddSecureTokenStore<TStore>(this IServiceCollection services)
        where TStore : class, ISecureTokenStore
    {
        services.TryAddSingleton<ISecureTokenStore, TStore>();
        return services;
    }
}
