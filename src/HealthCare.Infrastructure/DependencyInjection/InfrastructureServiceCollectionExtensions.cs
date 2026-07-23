using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace HealthCare.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public const string DefaultConnectionName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DefaultConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{DefaultConnectionName}' is not configured. " +
                "Set it via appsettings, user secrets, or environment variables. " +
                "See .env.example for the expected shape.");

        services.AddDbContext<HealthCareDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(HealthCareDbContext).Assembly.FullName);
            });
        });

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.SignIn.RequireConfirmedAccount = true;
            })
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<DevelopmentAdminOptions>(configuration.GetSection(DevelopmentAdminOptions.SectionName));
        services.Configure<DevelopmentPatientOptions>(configuration.GetSection(DevelopmentPatientOptions.SectionName));

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration section is required.");

        AccessTokenService.EnsureSigningKeyConfigured(jwtOptions.SigningKey);

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidateTokenReplay = false,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };
            });

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<ICurrentStaff>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<ICurrentPatient>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddScoped<ITenantAccessService, TenantAccessService>();

        services.AddScoped<IAuthorizationHandler, StaffUserHandler>();
        services.AddScoped<IAuthorizationHandler, OrganizationScopedHandler>();
        services.AddScoped<IAuthorizationHandler, ClinicScopedHandler>();
        services.AddScoped<IAuthorizationHandler, PatientUserHandler>();
        services.AddScoped<IAuthorizationHandler, PatientSelfScopeHandler>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Authenticated, policy =>
                policy.RequireAuthenticatedUser());

            options.AddPolicy(AuthorizationPolicies.PlatformAdmin, policy =>
                policy.RequireRole(AppRoles.PlatformAdmin));

            options.AddPolicy(AuthorizationPolicies.StaffUser, policy =>
                policy.Requirements.Add(new StaffUserRequirement()));

            options.AddPolicy(AuthorizationPolicies.OrganizationScoped, policy =>
                policy.Requirements.Add(new OrganizationScopedRequirement()));

            options.AddPolicy(AuthorizationPolicies.ClinicScoped, policy =>
                policy.Requirements.Add(new ClinicScopedRequirement()));

            options.AddPolicy(AuthorizationPolicies.PatientUser, policy =>
                policy.Requirements.Add(new PatientUserRequirement()));

            options.AddPolicy(AuthorizationPolicies.PatientSelfScope, policy =>
                policy.Requirements.Add(new PatientSelfScopeRequirement()));
        });

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAccessTokenService, AccessTokenService>();
        services.AddScoped<IRefreshTokenHasher, RefreshTokenHasher>();
        services.AddScoped<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPatientRegistrationService, PatientRegistrationService>();
        services.AddScoped<IPatientAccountLinker, PatientAccountLinker>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IClinicEnrollmentService, ClinicEnrollmentService>();
        services.AddScoped<IPatientClinicRegistrationService, PatientClinicRegistrationService>();
        services.AddScoped<IStaffPatientService, StaffPatientService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IClinicPublicLookup, ClinicPublicLookup>();
        services.AddScoped<ILocalPatientNumberGenerator, LocalPatientNumberGenerator>();
        services.AddSingleton<IDevelopmentConfirmationTokenStore, DevelopmentConfirmationTokenStore>();
        services.AddScoped<IAccountEmailSender, DevelopmentAccountEmailSender>();
        services.AddScoped<IRoleSeeder, RoleSeeder>();
        services.AddScoped<IDevelopmentAdminSeeder, DevelopmentAdminSeeder>();
        services.AddScoped<IDevelopmentPatientSeeder, DevelopmentPatientSeeder>();

        return services;
    }
}
