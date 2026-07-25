using System.Reflection;
using HealthCare.Mobile.Core;
using HealthCare.Mobile.Core.Configuration;
using HealthCare.Mobile.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCare.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        var configuration = BuildConfiguration();
        builder.Configuration.AddConfiguration(configuration);

        builder.Services.AddOptions<MobileAppOptions>()
            .Bind(configuration.GetSection(MobileAppOptions.SectionName))
            .PostConfigure(options =>
            {
                var errors = MobileAppOptionsValidator.Validate(options);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Invalid Mobile configuration: " + string.Join(" ", errors));
                }
            });

        builder.Services.AddSecureTokenStore<MauiSecureTokenStore>();
        builder.Services.AddHealthCareMobileCore();
        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static IConfiguration BuildConfiguration()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var configBuilder = new ConfigurationBuilder();

        AddEmbeddedJson(configBuilder, assembly, "HealthCare.Mobile.appsettings.json");

#if DEBUG
        AddEmbeddedJson(configBuilder, assembly, "HealthCare.Mobile.appsettings.Development.json");
#endif

        var overlay = Environment.GetEnvironmentVariable("HEALTHCARE_MOBILE_ENV");
        if (string.Equals(overlay, "Emulator", StringComparison.OrdinalIgnoreCase))
        {
            AddEmbeddedJson(configBuilder, assembly, "HealthCare.Mobile.appsettings.Emulator.json");
        }

        var apiOverride = Environment.GetEnvironmentVariable("HEALTHCARE_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(apiOverride))
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mobile:ApiBaseUrl"] = apiOverride.Trim().TrimEnd('/'),
            });
        }

        return configBuilder.Build();
    }

    private static void AddEmbeddedJson(ConfigurationBuilder builder, Assembly assembly, string logicalName)
    {
        var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream is not null)
        {
            builder.AddJsonStream(stream);
        }
    }
}
