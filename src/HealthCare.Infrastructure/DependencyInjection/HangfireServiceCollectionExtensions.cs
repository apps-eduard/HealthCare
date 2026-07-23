using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HealthCare.Infrastructure.DependencyInjection;

public static class HangfireServiceCollectionExtensions
{
    public const string DashboardPath = "/hangfire";

    public static IServiceCollection AddAppointmentReminders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddScoped<IAppointmentReminderSender, DevelopmentAppointmentReminderSender>();
        services.AddScoped<IAppointmentReminderScheduler, AppointmentReminderScheduler>();
        services.AddScoped<IAppointmentReminderProcessor, AppointmentReminderProcessor>();
        services.AddScoped<IAppointmentReminderRecoveryService, AppointmentReminderRecoveryService>();
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();

        var connectionString = configuration.GetConnectionString(InfrastructureServiceCollectionExtensions.DefaultConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{InfrastructureServiceCollectionExtensions.DefaultConnectionName}' is required for Hangfire.");

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

        if (environment.IsDevelopment())
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
                options.Queues = ["default"];
            });
        }

        return services;
    }

    public static IApplicationBuilder UseAppointmentReminderHangfire(
        this IApplicationBuilder app,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return app;
        }

        var recurring = app.ApplicationServices.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<AppointmentReminderHangfireJobs>(
            "appointment-reminder-recovery",
            j => j.RecoverOverdueRemindersAsync(CancellationToken.None),
            "*/5 * * * *");

        app.UseHangfireDashboard(DashboardPath, new DashboardOptions
        {
            Authorization = [new HangfireDashboardAuthFilter()],
            DashboardTitle = "HealthCare Jobs (Development)",
        });

        return app;
    }
}

/// <summary>
/// Hangfire dashboard requires an authenticated PLATFORM_ADMIN in Development.
/// </summary>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
               && http.User.IsInRole(AppRoles.PlatformAdmin);
    }
}
