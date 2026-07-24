using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace HealthCare.IntegrationTests;

/// <summary>
/// Process-wide integration-test host defaults.
/// Disables Hangfire workers by default and avoids disposing Serilog's static logger per host.
/// </summary>
internal static class IntegrationTestHost
{
    internal const string SkipStaticLoggerFlushEnvVar = "HEALTHCARE_SKIP_STATIC_LOGGER_FLUSH";
    internal const string IntegrationTestHostEnvVar = "HEALTHCARE_INTEGRATION_TEST_HOST";

    [ModuleInitializer]
    internal static void ConfigureProcessDefaults()
    {
        Environment.SetEnvironmentVariable(SkipStaticLoggerFlushEnvVar, "true");
        Environment.SetEnvironmentVariable(IntegrationTestHostEnvVar, "true");
    }

    /// <summary>
    /// Applies shared integration-test host settings. Hangfire workers stay off unless a test
    /// explicitly re-enables them (see HangfireHostingEndpointTests).
    /// </summary>
    public static void ApplyDefaultSettings(IWebHostBuilder builder, bool disableHangfireWorkers = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Ensure Program.cs can detect test hosts even when Environment is Development (seeders).
        builder.UseSetting("HealthCare:IntegrationTestHost", "true");

        if (!disableHangfireWorkers)
        {
            return;
        }

        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Hangfire:ScheduleRecurringJobs", "false");
        builder.UseSetting("Hangfire:Dashboard:Enabled", "false");
        builder.UseSetting("Hangfire:ServerName", "healthcare-integration-test");
    }

    public static WebApplicationFactory<Program> CreateFactory(
        Action<IWebHostBuilder> configure,
        bool disableHangfireWorkers = true)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApplyDefaultSettings(builder, disableHangfireWorkers);
            configure(builder);
        });
    }
}
