using FluentValidation;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Patients;

namespace HealthCare.Application.Patients;

public sealed class StaffPatientSearchRequestValidator : AbstractValidator<StaffPatientSearchRequest>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;

    public static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "registeredAtUtc",
        "localPatientNumber",
        "firstName",
        "lastName",
        "clinicPatientStatus",
        "patientIsActive",
    };

    public StaffPatientSearchRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode(PatientErrorCodes.InvalidSearch);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithErrorCode(PatientErrorCodes.InvalidSearch);

        RuleFor(x => x.Search)
            .MaximumLength(MaxSearchLength)
            .When(x => x.Search is not null)
            .WithErrorCode(PatientErrorCodes.InvalidSearch);

        RuleFor(x => x.LocalPatientNumber)
            .MaximumLength(64)
            .When(x => x.LocalPatientNumber is not null)
            .WithErrorCode(PatientErrorCodes.InvalidSearch);

        RuleFor(x => x.SortBy)
            .Must(sort => AllowedSortFields.Contains(sort))
            .WithErrorCode(PatientErrorCodes.InvalidSearch)
            .WithMessage("Unsupported sort field.");

        RuleFor(x => x.SortDirection)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase)
                       || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode(PatientErrorCodes.InvalidSearch)
            .WithMessage("Sort direction must be asc or desc.");

        RuleFor(x => x.ClinicPatientStatus)
            .Must(s => Enum.TryParse<ClinicPatientStatus>(s, ignoreCase: true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.ClinicPatientStatus))
            .WithErrorCode(PatientErrorCodes.InvalidSearch)
            .WithMessage("ClinicPatientStatus must be Active or Inactive.");
    }
}

public sealed class UpdateClinicPatientRequestValidator : AbstractValidator<UpdateClinicPatientRequest>
{
    public UpdateClinicPatientRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<ClinicPatientStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Active or Inactive.");
    }
}
