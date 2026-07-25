using FluentValidation;
using HealthCare.Contracts.Doctors;

namespace HealthCare.Application.Doctors;

public sealed class DoctorProfileQueryValidator : AbstractValidator<DoctorProfileQuery>
{
    public DoctorProfileQueryValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("ClinicId must be a non-empty GUID when provided.");

        RuleFor(x => x.DoctorStaffMemberId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("DoctorStaffMemberId must be a non-empty GUID when provided.");
    }
}

public sealed class UpdateDoctorProfileRequestValidator : AbstractValidator<UpdateDoctorProfileRequest>
{
    public UpdateDoctorProfileRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => x.HasAnyEditableField)
            .WithErrorCode(DoctorProfileErrorCodes.EmptyUpdate)
            .WithMessage("At least one doctor profile field must be provided.");

        When(x => x.DisplayNameSpecified && x.DisplayName is not null, () =>
        {
            RuleFor(x => x.DisplayName!).MaximumLength(200);
        });

        When(x => x.FirstNameSpecified, () =>
        {
            RuleFor(x => x.FirstName)
                .NotEmpty()
                .MaximumLength(100);
        });

        When(x => x.LastNameSpecified, () =>
        {
            RuleFor(x => x.LastName)
                .NotEmpty()
                .MaximumLength(100);
        });

        When(x => x.JobTitleSpecified && x.JobTitle is not null, () =>
        {
            RuleFor(x => x.JobTitle!).MaximumLength(150);
        });

        When(x => x.ContactPhoneSpecified && x.ContactPhone is not null, () =>
        {
            RuleFor(x => x.ContactPhone!).MaximumLength(30);
        });
    }
}
