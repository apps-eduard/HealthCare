using HealthCare.Application.Patients;
using HealthCare.Domain.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Patients;

public interface IDevelopmentPatientSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent Development-only patient + clinic isolation seed data.
/// </summary>
public sealed class DevelopmentPatientSeeder : IDevelopmentPatientSeeder
{
    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPatientAccountLinker _linker;
    private readonly DevelopmentPatientOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevelopmentPatientSeeder> _logger;

    public DevelopmentPatientSeeder(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IPatientAccountLinker linker,
        IOptions<DevelopmentPatientOptions> options,
        IHostEnvironment environment,
        ILogger<DevelopmentPatientSeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _linker = linker;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogDebug("Development patient seed skipped: email/password not configured");
            return;
        }

        if (_options.Password.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Development patient seed skipped: password placeholder not replaced");
            return;
        }

        var organization = await EnsureOrganizationAsync(cancellationToken);
        var clinicA = await EnsureClinicAsync(
            organization.Id,
            _options.ClinicName,
            _options.ClinicSlug,
            cancellationToken);
        var clinicB = await EnsureClinicAsync(
            organization.Id,
            _options.OtherClinicName,
            _options.OtherClinicSlug,
            cancellationToken);

        var patientUser = await EnsurePatientUserAsync(cancellationToken);
        var patient = await EnsurePatientProfileAsync(cancellationToken);
        await _linker.LinkUserToPatientAsync(patientUser.Id, patient.Id, cancellationToken);

        await EnsureClinicPatientAsync(clinicA.Id, patient.Id, _options.LocalPatientNumber, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.StaffEmail) && !string.IsNullOrWhiteSpace(_options.StaffPassword))
        {
            await EnsureStaffAsync(
                _options.StaffEmail,
                _options.StaffPassword,
                organization.Id,
                clinicA.Id,
                AppRoles.Doctor,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_options.OtherClinicStaffEmail)
            && !string.IsNullOrWhiteSpace(_options.OtherClinicStaffPassword))
        {
            await EnsureStaffAsync(
                _options.OtherClinicStaffEmail,
                _options.OtherClinicStaffPassword,
                organization.Id,
                clinicB.Id,
                AppRoles.Doctor,
                cancellationToken);
        }

        _logger.LogInformation("Development patient seed completed");
    }

    private async Task<Organization> EnsureOrganizationAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Organizations
            .SingleOrDefaultAsync(o => o.Slug == _options.OrganizationSlug, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = _options.OrganizationName,
            Slug = _options.OrganizationSlug,
            Status = OrganizationStatus.Active,
        };
        _dbContext.Organizations.Add(organization);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return organization;
    }

    private async Task<Clinic> EnsureClinicAsync(
        Guid organizationId,
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Clinics
            .SingleOrDefaultAsync(c => c.Slug == slug, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Slug = slug,
            IsActive = true,
        };
        _dbContext.Clinics.Add(clinic);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return clinic;
    }

    private async Task<ApplicationUser> EnsurePatientUserAsync(CancellationToken cancellationToken)
    {
        var existing = await _userManager.FindByEmailAsync(_options.Email!);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = _options.Email,
            Email = _options.Email,
            EmailConfirmed = true,
            IsActive = true,
        };

        var createResult = await _userManager.CreateAsync(user, _options.Password!);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to seed development patient user: {errors}");
        }

        var roleResult = await _userManager.AddToRoleAsync(user, AppRoles.Patient);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to assign PATIENT role: {errors}");
        }

        return user;
    }

    private async Task<Patient> EnsurePatientProfileAsync(CancellationToken cancellationToken)
    {
        var existingUser = await _userManager.FindByEmailAsync(_options.Email!);
        if (existingUser is not null)
        {
            var linked = await _dbContext.Patients
                .SingleOrDefaultAsync(p => p.UserId == existingUser.Id, cancellationToken);
            if (linked is not null)
            {
                return linked;
            }
        }

        var byName = await _dbContext.Patients
            .SingleOrDefaultAsync(
                p => p.FirstName == _options.FirstName
                    && p.LastName == _options.LastName
                    && p.UserId == null,
                cancellationToken);
        if (byName is not null)
        {
            return byName;
        }

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = _options.FirstName,
            LastName = _options.LastName,
            PreferredLanguage = "en",
            IsActive = true,
        };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return patient;
    }

    private async Task EnsureClinicPatientAsync(
        Guid clinicId,
        Guid patientId,
        string localNumber,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.ClinicPatients
            .AnyAsync(cp => cp.ClinicId == clinicId && cp.PatientId == patientId, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patientId,
            LocalPatientNumber = localNumber,
            Status = ClinicPatientStatus.Active,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureStaffAsync(
        string email,
        string password,
        Guid organizationId,
        Guid clinicId,
        string role,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
            };

            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to seed staff user {email}: {errors}");
            }

            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to assign role {role} to {email}: {errors}");
            }
        }

        var membershipExists = await _dbContext.StaffMembers
            .AnyAsync(s => s.UserId == user.Id, cancellationToken);
        if (membershipExists)
        {
            return;
        }

        _dbContext.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = organizationId,
            ClinicId = clinicId,
            Role = role,
            JobTitle = role,
            IsActive = true,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class DevelopmentPatientSeederExtensions
{
    public static async Task SeedDevelopmentPatientAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentPatientSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
