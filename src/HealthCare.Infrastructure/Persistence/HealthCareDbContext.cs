using HealthCare.Domain.Appointments;
using HealthCare.Domain.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.Infrastructure.Persistence;

/// <summary>
/// Application database context including ASP.NET Core Identity.
/// Patient is a global identity entity — tenant isolation for clinic-owned patient data
/// is enforced via ClinicPatient predicates in application services (EF global filters deferred).
/// </summary>
public sealed class HealthCareDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public HealthCareDbContext(DbContextOptions<HealthCareDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<ClinicPatient> ClinicPatients => Set<ClinicPatient>();

    public DbSet<ClinicPatientNumberSequence> ClinicPatientNumberSequences => Set<ClinicPatientNumberSequence>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<DoctorAvailability> DoctorAvailabilities => Set<DoctorAvailability>();

    public DbSet<DoctorAvailabilityException> DoctorAvailabilityExceptions => Set<DoctorAvailabilityException>();

    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("public");
        modelBuilder.HasAnnotation("HealthCare:SchemaVersion", "9");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HealthCareDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    private void ApplyTimestamps()
    {
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case ApplicationUser user:
                    if (entry.State == EntityState.Added)
                    {
                        user.CreatedAtUtc = utcNow;
                    }

                    user.UpdatedAtUtc = utcNow;
                    break;
                case Organization organization:
                    if (entry.State == EntityState.Added)
                    {
                        organization.CreatedAtUtc = utcNow;
                    }

                    organization.UpdatedAtUtc = utcNow;
                    break;
                case Clinic clinic:
                    if (entry.State == EntityState.Added)
                    {
                        clinic.CreatedAtUtc = utcNow;
                    }

                    clinic.UpdatedAtUtc = utcNow;
                    break;
                case StaffMember staffMember:
                    if (entry.State == EntityState.Added)
                    {
                        staffMember.CreatedAtUtc = utcNow;
                    }

                    staffMember.UpdatedAtUtc = utcNow;
                    break;
                case Patient patient:
                    if (entry.State == EntityState.Added)
                    {
                        patient.CreatedAtUtc = utcNow;
                    }

                    patient.UpdatedAtUtc = utcNow;
                    break;
                case ClinicPatient clinicPatient:
                    if (entry.State == EntityState.Added)
                    {
                        clinicPatient.RegisteredAtUtc = utcNow;
                    }

                    clinicPatient.UpdatedAtUtc = utcNow;
                    break;
                case Appointment appointment:
                    if (entry.State == EntityState.Added)
                    {
                        appointment.CreatedAtUtc = utcNow;
                    }

                    appointment.UpdatedAtUtc = utcNow;
                    break;
                case DoctorAvailability availability:
                    if (entry.State == EntityState.Added)
                    {
                        availability.CreatedAtUtc = utcNow;
                    }

                    availability.UpdatedAtUtc = utcNow;
                    break;
                case DoctorAvailabilityException availabilityException:
                    if (entry.State == EntityState.Added)
                    {
                        availabilityException.CreatedAtUtc = utcNow;
                    }

                    availabilityException.UpdatedAtUtc = utcNow;
                    break;
                case AppointmentReminder reminder:
                    if (entry.State == EntityState.Added)
                    {
                        reminder.CreatedAtUtc = utcNow;
                    }

                    reminder.UpdatedAtUtc = utcNow;
                    break;
            }
        }
    }
}
