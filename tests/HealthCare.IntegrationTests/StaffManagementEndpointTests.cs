using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Staff;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class StaffManagementEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_staff_mgmt_test")
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
    public async Task Anonymous_Staff_List_Returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/staff-management/staff")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Staff_List_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/staff-management/staff")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_Admin_Can_List_And_Create_Receptionist()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        var list = await client.GetFromJsonAsync<Contracts.Common.PagedResponse<StaffSummaryResponse>>(
            "/api/v1/staff-management/staff");
        list.Should().NotBeNull();
        list!.Items.Should().OnlyContain(i => i.ClinicId != Guid.Empty);

        var create = await client.PostAsJsonAsync("/api/v1/staff-management/staff", new CreateStaffRequest
        {
            Email = $"recv.{Guid.NewGuid():N}@healthcare.local",
            FirstName = "Rec",
            LastName = "Eption",
            Role = AppRoles.Receptionist,
            TemporaryPassword = "TempPass_Staff_99!",
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);

        var deny = await client.PostAsync(
            $"/api/v1/staff-management/staff/{list.Items.First().StaffMemberId}/roles/{AppRoles.OrganizationAdmin}",
            null);
        deny.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
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
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminEmail", "orgadmin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminPassword", "ChangeMe_OrgAdmin_1!");
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminEmail", "clinicadmin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminPassword", "ChangeMe_ClinicAdmin_1!");
                builder.UseSetting("Hangfire:Enabled", "false");
                builder.UseSetting("Hangfire:ScheduleRecurringJobs", "false");
                builder.UseSetting("Hangfire:Dashboard:Enabled", "false");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(_connectionString));
                });
            });

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
