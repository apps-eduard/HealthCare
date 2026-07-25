using FluentValidation;
using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public sealed class PatientClinicSearchRequestValidator : AbstractValidator<PatientClinicSearchRequest>
{
    public const int MaxSearchLength = 100;
    public const int MaxPageSize = 50;

    public PatientClinicSearchRequestValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(MaxSearchLength)
            .When(x => x.Search is not null);

        RuleFor(x => x.Specialty)
            .MaximumLength(150)
            .When(x => x.Specialty is not null);

        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize);
    }
}

public interface IPatientClinicDirectoryService
{
    Task<Contracts.Common.PagedResponse<PatientClinicListItemResponse>> SearchAsync(
        PatientClinicSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<PatientClinicDetailResponse> GetByClinicCodeAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);
}
