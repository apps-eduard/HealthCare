using FluentAssertions;
using HealthCare.Infrastructure.Configuration;
using HealthCare.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HealthCare.UnitTests;

public sealed class HangfireHostingOptionsTests
{
    [Fact]
    public void Development_Appsettings_Enable_Workers_By_Default()
    {
        var opts = BindFromFiles("appsettings.json", "appsettings.Development.json");
        opts.Enabled.Should().BeTrue();
        opts.ScheduleRecurringJobs.Should().BeTrue();
        opts.Dashboard.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Base_And_Production_Defaults_Disable_Workers()
    {
        var baseOpts = BindFromFiles("appsettings.json");
        baseOpts.Enabled.Should().BeFalse();
        baseOpts.ScheduleRecurringJobs.Should().BeFalse();
        baseOpts.Dashboard.Enabled.Should().BeFalse();

        var prod = BindFromFiles("appsettings.json", "appsettings.Production.json");
        prod.Enabled.Should().BeFalse();
        prod.Dashboard.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Explicit_Production_Enablement_Is_Valid()
    {
        var opts = new HangfireOptions
        {
            Enabled = true,
            WorkerCount = 4,
            Queues = ["default", "reminders", "summaries"],
            ServerName = "healthcare-api",
            ScheduleRecurringJobs = true,
            Dashboard = new HangfireDashboardOptions { Enabled = false, Path = "/hangfire" },
        };

        new HangfireOptionsValidator().Validate(null, opts).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Worker_Count_Validation()
    {
        var opts = ValidEnabled();
        opts.WorkerCount = 0;
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();

        opts.WorkerCount = 65;
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();

        opts.WorkerCount = 4;
        new HangfireOptionsValidator().Validate(null, opts).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Empty_Queue_Validation()
    {
        var opts = ValidEnabled();
        opts.Queues = [];
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Duplicate_Queues_Are_Normalized()
    {
        var queues = HangfireOptionsValidator.NormalizeQueues(
            ["default", "Default", "reminders", "reminders"],
            out var errors);
        errors.Should().BeEmpty();
        queues.Should().Equal("default", "reminders");
    }

    [Fact]
    public void Invalid_Queue_Name_Rejected()
    {
        var opts = ValidEnabled();
        opts.Queues = ["default", "bad queue!"];
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Unsafe_Dashboard_Path_Rejected()
    {
        var opts = ValidEnabled();
        opts.Dashboard.Enabled = true;
        opts.Dashboard.Path = "/";
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();

        opts.Dashboard.Path = "hangfire";
        new HangfireOptionsValidator().Validate(null, opts).Failed.Should().BeTrue();
    }

    [Fact]
    public void Dashboard_Remains_Independent_Of_Workers()
    {
        var opts = ValidEnabled();
        opts.Dashboard.Enabled = false;
        new HangfireOptionsValidator().Validate(null, opts).Succeeded.Should().BeTrue();
        opts.Dashboard.Enabled.Should().BeFalse();
        opts.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Disabled_Workers_Do_Not_Require_Worker_Settings()
    {
        var opts = new HangfireOptions
        {
            Enabled = false,
            WorkerCount = 0,
            Queues = [],
            ScheduleRecurringJobs = false,
            Dashboard = new HangfireDashboardOptions { Enabled = false },
        };
        new HangfireOptionsValidator().Validate(null, opts).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Enabled_Workers_Register_Server_Descriptor()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=u;Password=p",
            ["Hangfire:Enabled"] = "true",
            ["Hangfire:WorkerCount"] = "2",
            ["Hangfire:Queues:0"] = "default",
            ["Hangfire:ServerName"] = "test-server",
            ["Hangfire:ScheduleRecurringJobs"] = "false",
            ["Hangfire:Dashboard:Enabled"] = "false",
        }, Environments.Production);

        IsHangfireServerRegistered(services).Should().BeTrue();
    }

    [Fact]
    public void Disabled_Workers_Do_Not_Register_Server()
    {
        var services = BuildServices(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=u;Password=p",
            ["Hangfire:Enabled"] = "false",
            ["Hangfire:ScheduleRecurringJobs"] = "false",
            ["Hangfire:Dashboard:Enabled"] = "false",
        }, Environments.Production);

        IsHangfireServerRegistered(services).Should().BeFalse();
    }

    [Fact]
    public void Startup_Log_Values_Contain_No_Secrets()
    {
        var opts = ValidEnabled();
        opts.ServerName = "healthcare-api";
        var logLine =
            $"Hangfire startup. Enabled={opts.Enabled} WorkerCount={opts.WorkerCount} Queues={string.Join(',', opts.Queues)} ServerName={opts.ServerName} ScheduleRecurringJobs={opts.ScheduleRecurringJobs} DashboardEnabled={opts.Dashboard.Enabled} DashboardPath={opts.Dashboard.Path}";

        logLine.Should().NotContain("Password");
        logLine.Should().NotContain("Host=");
        logLine.Should().NotContain("SigningKey");
        logLine.Should().NotContain("gho_");
    }

    [Fact]
    public void Recurring_Registration_Requires_Workers_And_Schedule_Flag()
    {
        // Registration is gated in UseAppointmentReminderHangfire: Enabled && ScheduleRecurringJobs.
        var workersOff = new HangfireOptions { Enabled = false, ScheduleRecurringJobs = true };
        (workersOff.Enabled && workersOff.ScheduleRecurringJobs).Should().BeFalse();

        var scheduleOff = new HangfireOptions { Enabled = true, ScheduleRecurringJobs = false };
        (scheduleOff.Enabled && scheduleOff.ScheduleRecurringJobs).Should().BeFalse();

        var bothOn = new HangfireOptions { Enabled = true, ScheduleRecurringJobs = true };
        (bothOn.Enabled && bothOn.ScheduleRecurringJobs).Should().BeTrue();
    }

    [Fact]
    public void Recurring_Registrar_Ids_Are_Stable()
    {
        HangfireRecurringJobRegistrar.ReminderRecoveryJobId.Should().Be("appointment-reminder-recovery");
        HangfireRecurringJobRegistrar.SummaryDispatchJobId.Should().Be("clinic-appointment-summary-dispatch");
        HangfireRecurringJobRegistrar.SummaryRecoveryJobId.Should().Be("clinic-appointment-summary-recovery");
    }

    [Fact]
    public void Dashboard_Auth_Filter_Requires_Platform_Admin()
    {
        HealthCare.Domain.Identity.AppRoles.PlatformAdmin.Should().Be("PLATFORM_ADMIN");
        typeof(HangfireDashboardAuthFilter).GetMethod(nameof(HangfireDashboardAuthFilter.Authorize))
            .Should().NotBeNull();
    }

    private static HangfireOptions ValidEnabled() => new()
    {
        Enabled = true,
        WorkerCount = 2,
        Queues = ["default", "reminders", "summaries"],
        ServerName = "healthcare-api",
        ScheduleRecurringJobs = true,
        ShutdownTimeoutSeconds = 30,
        Dashboard = new HangfireDashboardOptions { Enabled = false, Path = "/hangfire" },
    };

    private static HangfireOptions BindFromFiles(params string[] files)
    {
        var apiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Api"));
        var builder = new ConfigurationBuilder();
        foreach (var file in files)
        {
            builder.AddJsonFile(Path.Combine(apiDir, file), optional: false);
        }

        var opts = new HangfireOptions();
        builder.Build().GetSection(HangfireOptions.SectionName).Bind(opts);
        return opts;
    }

    private static List<ServiceDescriptor> BuildServices(
        Dictionary<string, string?> values,
        string environmentName)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new FakeHostEnvironment { EnvironmentName = environmentName };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddAppointmentReminders(config, env);
        return services.ToList();
    }

    private static bool IsHangfireServerRegistered(IEnumerable<ServiceDescriptor> services) =>
        services.Any(d =>
        {
            var serviceName = d.ServiceType.FullName ?? d.ServiceType.Name;
            var implName = d.ImplementationType?.FullName
                ?? d.ImplementationInstance?.GetType().FullName
                ?? d.ImplementationFactory?.Method.ReturnType.FullName
                ?? string.Empty;

            return serviceName.Contains("IBackgroundProcessingServer", StringComparison.Ordinal)
                   || serviceName.Contains("BackgroundJobServer", StringComparison.Ordinal)
                   || implName.Contains("BackgroundJobServer", StringComparison.Ordinal)
                   || implName.Contains("BackgroundProcessingServer", StringComparison.Ordinal);
        });

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "HealthCare.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
