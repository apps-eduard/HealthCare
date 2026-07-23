using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Configuration;
using HealthCare.Infrastructure.Health;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.DependencyInjection;

public static class HangfireServiceCollectionExtensions
{
    public static IServiceCollection AddAppointmentReminders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<HangfireOptions>, HangfireOptionsValidator>();
        services.AddOptions<HangfireOptions>()
            .Bind(configuration.GetSection(HangfireOptions.SectionName))
            .ValidateOnStart();

        services.PostConfigure<HangfireOptions>(options =>
        {
            var queues = HangfireOptionsValidator.NormalizeQueues(options.Queues, out _);
            if (queues.Count > 0)
            {
                options.Queues = queues.ToArray();
            }

            if (!string.IsNullOrWhiteSpace(options.ServerName))
            {
                options.ServerName = options.ServerName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(options.Dashboard.Path))
            {
                options.Dashboard.Path = options.Dashboard.Path.Trim();
            }
        });

        if (environment.IsDevelopment())
        {
            services.AddScoped<IAppointmentReminderSender, DevelopmentAppointmentReminderSender>();
            services.AddScoped<IClinicAppointmentSummarySender, DevelopmentClinicAppointmentSummarySender>();
        }
        else
        {
            services.AddScoped<IAppointmentReminderSender, NoOpAppointmentReminderSender>();
            services.AddScoped<IClinicAppointmentSummarySender, NoOpClinicAppointmentSummarySender>();
        }

        services.AddScoped<IAppointmentReminderScheduler, AppointmentReminderScheduler>();
        services.AddScoped<IAppointmentReminderProcessor, AppointmentReminderProcessor>();
        services.AddScoped<IAppointmentReminderRecoveryService, AppointmentReminderRecoveryService>();
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();

        services.AddScoped<IClinicAppointmentSummaryBuilder, ClinicAppointmentSummaryBuilder>();
        services.AddScoped<IClinicAppointmentSummaryDispatcher, ClinicAppointmentSummaryDispatcher>();
        services.AddScoped<IClinicAppointmentSummaryProcessor, ClinicAppointmentSummaryProcessor>();
        services.AddScoped<IClinicAppointmentSummaryRecoveryService, ClinicAppointmentSummaryRecoveryService>();
        services.AddScoped<IClinicAppointmentSummaryService, ClinicAppointmentSummaryService>();
        services.AddScoped<IClinicAppointmentSummaryJobs, HangfireClinicAppointmentSummaryJobs>();

        var connectionString = configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.DefaultConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{InfrastructureServiceCollectionExtensions.DefaultConnectionName}' is required for Hangfire.");

        // Storage + client always registered so the API can enqueue jobs for a local or external worker.
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                }));

        services.AddScoped<IReminderBackgroundJobs, HangfireReminderBackgroundJobs>();

        var hangfire = configuration.GetSection(HangfireOptions.SectionName).Get<HangfireOptions>() ?? new HangfireOptions();
        HangfireOptionsValidator.NormalizeQueues(hangfire.Queues, out _);

        if (hangfire.Enabled)
        {
            var queues = HangfireOptionsValidator.NormalizeQueues(hangfire.Queues, out var queueErrors);
            if (queueErrors.Count > 0 || queues.Count == 0)
            {
                throw new OptionsValidationException(
                    HangfireOptions.SectionName,
                    typeof(HangfireOptions),
                    queueErrors.Count > 0 ? queueErrors : ["Hangfire:Queues must contain at least one queue when Hangfire is enabled."]);
            }

            services.AddHangfireServer(options =>
            {
                options.WorkerCount = hangfire.WorkerCount;
                options.Queues = queues.ToArray();
                options.ServerName = string.IsNullOrWhiteSpace(hangfire.ServerName)
                    ? "healthcare-api"
                    : hangfire.ServerName.Trim();
                options.ShutdownTimeout = TimeSpan.FromSeconds(hangfire.ShutdownTimeoutSeconds);
            });
        }

        services.AddHealthChecks()
            .AddCheck<HangfireStorageHealthCheck>("hangfire_storage", tags: ["ready", "hangfire"])
            .AddCheck<HangfireWorkerStateHealthCheck>("hangfire_worker", tags: ["hangfire"]);

        return services;
    }

    public static WebApplication UseAppointmentReminderHangfire(
        this WebApplication app,
        IHostEnvironment environment)
    {
        var options = app.Services.GetRequiredService<IOptions<HangfireOptions>>().Value;
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("HealthCare.Hangfire");

        logger.LogInformation(
            "Hangfire startup. Enabled={Enabled} WorkerCount={WorkerCount} Queues={Queues} ServerName={ServerName} ScheduleRecurringJobs={ScheduleRecurringJobs} DashboardEnabled={DashboardEnabled} DashboardPath={DashboardPath}",
            options.Enabled,
            options.WorkerCount,
            string.Join(',', options.Queues),
            options.ServerName,
            options.ScheduleRecurringJobs,
            options.Dashboard.Enabled,
            options.Dashboard.Path);

        if (!options.Enabled)
        {
            logger.LogInformation(
                "Hangfire workers intentionally disabled. Job enqueueing remains available for an external worker process.");
        }

        if (HangfireRecurringJobRegistrar.IsDesignTime(environment))
        {
            logger.LogInformation("Skipping Hangfire recurring-job registration (design-time host).");
            return app;
        }

        if (options.Enabled && options.ScheduleRecurringJobs)
        {
            var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
            HangfireRecurringJobRegistrar.Register(recurring, logger);
        }
        else if (options.ScheduleRecurringJobs && !options.Enabled)
        {
            logger.LogWarning(
                "Hangfire ScheduleRecurringJobs=true ignored because Hangfire.Enabled=false. Register recurring jobs on a host with workers enabled (API or dedicated worker).");
        }
        else
        {
            logger.LogInformation("Hangfire recurring-job registration skipped (ScheduleRecurringJobs=false or workers disabled).");
        }

        if (options.Dashboard.Enabled)
        {
            app.MapHangfireDashboard(options.Dashboard.Path, new DashboardOptions
            {
                Authorization = [new HangfireDashboardAuthFilter()],
                DashboardTitle = environment.IsDevelopment()
                    ? "HealthCare Jobs (Development)"
                    : "HealthCare Jobs",
                IgnoreAntiforgeryToken = false,
            });

            logger.LogInformation(
                "Hangfire dashboard enabled at {DashboardPath} (PLATFORM_ADMIN only).",
                options.Dashboard.Path);
        }
        else
        {
            logger.LogInformation("Hangfire dashboard disabled.");
        }

        return app;
    }
}

/// <summary>
/// Hangfire dashboard requires an authenticated PLATFORM_ADMIN.
/// </summary>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        if (http.User.Identity?.IsAuthenticated != true
            || !http.User.IsInRole(AppRoles.PlatformAdmin))
        {
            return false;
        }

        var permissions = http.RequestServices.GetService(typeof(IPermissionService)) as IPermissionService;
        return permissions?.HasPermission(Permissions.Hangfire.Dashboard) == true;
    }
}
