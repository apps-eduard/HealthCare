using HealthCare.Application.Patients;
using HealthCare.Domain.Clinics;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.Infrastructure.Clinics;

public sealed class ClinicPublicLookup : IClinicPublicLookup
{
    private readonly HealthCareDbContext _dbContext;

    public ClinicPublicLookup(HealthCareDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Clinic?> FindByPublicCodeAsync(
        string clinicCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = clinicCode.Trim().ToLowerInvariant();
        return await _dbContext.Clinics
            .AsNoTracking()
            .Include(c => c.Organization)
            .SingleOrDefaultAsync(c => c.Slug == normalized, cancellationToken);
    }
}
