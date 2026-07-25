using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class PatientClinicDirectoryService : IPatientClinicDirectoryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentPatient _currentPatient;
    private readonly IClinicPublicLookup _clinicLookup;
    private readonly ILogger<PatientClinicDirectoryService> _logger;

    public PatientClinicDirectoryService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentPatient currentPatient,
        IClinicPublicLookup clinicLookup,
        ILogger<PatientClinicDirectoryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentPatient = currentPatient;
        _clinicLookup = clinicLookup;
        _logger = logger;
    }

    public async Task<PagedResponse<PatientClinicListItemResponse>> SearchAsync(
        PatientClinicSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureLinkedPatient();

        var patientId = _currentPatient.PatientId!.Value;
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? 20
            : Math.Min(request.PageSize, PatientClinicSearchRequestValidator.MaxPageSize);

        var search = NormalizeOptional(request.Search);
        var specialty = NormalizeOptional(request.Specialty);

        var query = _dbContext.Clinics
            .AsNoTracking()
            .Where(c => c.IsActive
                        && c.Organization != null
                        && c.Organization.Status == OrganizationStatus.Active);

        if (search is not null)
        {
            var term = search.ToLowerInvariant();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || (c.City != null && c.City.ToLower().Contains(term))
                || (c.Address != null && c.Address.ToLower().Contains(term))
                || (c.AddressLine1 != null && c.AddressLine1.ToLower().Contains(term)));
        }

        if (specialty is not null)
        {
            var specialtyTerm = specialty.ToLowerInvariant();
            query = query.Where(c =>
                c.Specialty != null && c.Specialty.ToLower().Contains(specialtyTerm));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Slug,
                c.Name,
                c.City,
                c.Specialty,
                c.TimeZoneId,
                IsEnrolled = _dbContext.ClinicPatients.Any(cp =>
                    cp.ClinicId == c.Id && cp.PatientId == patientId),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new PatientClinicListItemResponse
            {
                ClinicCode = r.Slug,
                Name = r.Name,
                City = r.City,
                Specialty = r.Specialty,
                TimeZoneId = r.TimeZoneId,
                IsEnrolled = r.IsEnrolled,
            })
            .ToList();

        _logger.LogInformation(
            "Patient clinic directory search. UserId={UserId} Page={Page} PageSize={PageSize} Total={Total} SearchPresent={SearchPresent}",
            _currentUser.UserId,
            page,
            pageSize,
            totalCount,
            search is not null);

        return PagedResponse<PatientClinicListItemResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<PatientClinicDetailResponse> GetByClinicCodeAsync(
        string clinicCode,
        CancellationToken cancellationToken = default)
    {
        EnsureLinkedPatient();

        var patientId = _currentPatient.PatientId!.Value;
        var clinic = await _clinicLookup.FindByPublicCodeAsync(clinicCode, cancellationToken);
        if (clinic is null || !clinic.IsActive)
        {
            _logger.LogInformation(
                "Patient clinic detail denied. UserId={UserId} Reason={ReasonCode}",
                _currentUser.UserId,
                PatientErrorCodes.ClinicCodeInvalid);
            throw PatientClinicRegistrationException.InvalidClinicCode();
        }

        if (clinic.Organization is null || clinic.Organization.Status != OrganizationStatus.Active)
        {
            _logger.LogInformation(
                "Patient clinic detail denied. UserId={UserId} Reason={ReasonCode}",
                _currentUser.UserId,
                PatientErrorCodes.OrganizationInactive);
            throw PatientClinicRegistrationException.OrganizationInactive();
        }

        var enrollment = await _dbContext.ClinicPatients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.ClinicId == clinic.Id && cp.PatientId == patientId,
                cancellationToken);

        return new PatientClinicDetailResponse
        {
            ClinicCode = clinic.Slug,
            Name = clinic.Name,
            Specialty = clinic.Specialty,
            Description = clinic.Description,
            City = clinic.City,
            Address = FormatPublicAddress(clinic.Address, clinic.AddressLine1, clinic.AddressLine2, clinic.City),
            PhoneNumber = clinic.PhoneNumber,
            Email = clinic.Email,
            TimeZoneId = clinic.TimeZoneId,
            IsEnrolled = enrollment is not null,
            EnrollmentStatus = enrollment?.Status.ToString(),
        };
    }

    private void EnsureLinkedPatient()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsInRole(AppRoles.Patient))
        {
            throw AuthorizationException.Forbidden();
        }

        if (!_currentPatient.HasLinkedPatient || _currentPatient.PatientId is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? FormatPublicAddress(
        string? address,
        string? line1,
        string? line2,
        string? city)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            return address.Trim();
        }

        var parts = new[] { line1, line2, city }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }
}
