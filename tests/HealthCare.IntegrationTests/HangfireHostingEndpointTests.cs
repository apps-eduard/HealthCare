using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Hangfire.Storage;
using HealthCare.Contracts.Identity;
using HealthCare.Infrastructure.DependencyInjection;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class HangfireHostingEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_hangfire_hosting_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var migrateDb = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(_connectionString).Options);
        await migrateDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Api_Starts_With_Hangfire_Disabled()
    {
        await using var factory = CreateFactory(enabled: false, dashboard: false);
        var client = factory.CreateClient();
        var health = await client.GetAsync("/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);

        var dash = await client.GetAsync("/hangfire");
        dash.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Api_Starts_With_Hangfire_Enabled()
    {
        await using var factory = CreateFactory(enabled: true, dashboard: false, schedule: true);
        var client = factory.CreateClient();
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dashboard_Anonymous_Returns_401_When_Enabled()
    {
        await using var factory = CreateFactory(enabled: true, dashboard: true);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/hangfire");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, HttpStatusCode.Forbidden);
        // Hangfire dashboard auth filter denies anonymous → typically 401/403 from middleware pipeline.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dashboard_Non_Admin_Forbidden_Or_Denied()
    {
        await using var factory = CreateFactory(enabled: true, dashboard: true);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticateAsync(client, "doctor.a@healthcare.local", "ChangeMe_DoctorA_1!");
        var response = await client.GetAsync("/hangfire");
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dashboard_Platform_Admin_May_Access()
    {
        await using var factory = CreateFactory(enabled: true, dashboard: true);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");
        var response = await client.GetAsync("/hangfire");
        // Hangfire may return 200 HTML for authorized users.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Invalid_Enabled_WorkerCount_Fails_Startup()
    {
        var act = async () =>
        {
            await using var factory = CreateFactory(enabled: true, dashboard: false, workerCount: "0");
            _ = factory.CreateClient();
        };

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Recurring_Jobs_Registered_Once_Not_Duplicated()
    {
        await using var factory = CreateFactory(enabled: true, dashboard: false, schedule: true);
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var storage = Hangfire.JobStorage.Current;
        using var connection = storage.GetConnection();
        var jobs = connection.GetRecurringJobs();
        var ids = jobs.Select(j => j.Id).ToList();

        ids.Should().Contain(HangfireRecurringJobRegistrar.ReminderRecoveryJobId);
        ids.Should().Contain(HangfireRecurringJobRegistrar.SummaryDispatchJobId);
        ids.Should().Contain(HangfireRecurringJobRegistrar.SummaryRecoveryJobId);
        ids.Count(id => id == HangfireRecurringJobRegistrar.ReminderRecoveryJobId).Should().Be(1);

        // Second registration (AddOrUpdate) must remain idempotent.
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .CreateLogger("test");
        HangfireRecurringJobRegistrar.Register(
            scope.ServiceProvider.GetRequiredService<Hangfire.IRecurringJobManager>(),
            logger);

        using var connection2 = storage.GetConnection();
        var jobs2 = connection2.GetRecurringJobs();
        jobs2.Count(j => j.Id == HangfireRecurringJobRegistrar.ReminderRecoveryJobId).Should().Be(1);
        jobs2.Count(j => j.Id == HangfireRecurringJobRegistrar.SummaryDispatchJobId).Should().Be(1);
        jobs2.Count(j => j.Id == HangfireRecurringJobRegistrar.SummaryRecoveryJobId).Should().Be(1);
    }

    private WebApplicationFactory<Program> CreateFactory(
        bool enabled,
        bool dashboard,
        bool schedule = false,
        string workerCount = "2")
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
                builder.UseSetting("Jwt:Issuer", "HealthCare");
                builder.UseSetting("Jwt:Audience", "HealthCare");
                builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
                builder.UseSetting("DevelopmentSeed:Admin:Email", "admin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Admin:Password", "ChangeMe_Admin_1!");
                builder.UseSetting("DevelopmentSeed:Patient:Email", "patient@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:Password", "ChangeMe_Patient_1!");
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", "doctor.a@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", "ChangeMe_DoctorA_1!");
                builder.UseSetting("Hangfire:Enabled", enabled ? "true" : "false");
                builder.UseSetting("Hangfire:WorkerCount", workerCount);
                builder.UseSetting("Hangfire:Queues:0", "default");
                builder.UseSetting("Hangfire:Queues:1", "reminders");
                builder.UseSetting("Hangfire:Queues:2", "summaries");
                builder.UseSetting("Hangfire:ServerName", "healthcare-test");
                builder.UseSetting("Hangfire:ScheduleRecurringJobs", schedule ? "true" : "false");
                builder.UseSetting("Hangfire:Dashboard:Enabled", dashboard ? "true" : "false");
                builder.UseSetting("Hangfire:Dashboard:Path", "/hangfire");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(_connectionString));
                });
            });
    }

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
