using FluentValidation;
using HealthCare.Contracts.Patients;

namespace HealthCare.Application.Patients;

public sealed class UpdatePatientProfileRequestValidator : AbstractValidator<UpdatePatientProfileRequest>
{
    public UpdatePatientProfileRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => x.HasAnyEditableField)
            .WithErrorCode(PatientErrorCodes.EmptyProfileUpdate)
            .WithMessage("At least one profile field must be provided.");

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

        When(x => x.MiddleNameSpecified && x.MiddleName is not null, () =>
        {
            RuleFor(x => x.MiddleName!).MaximumLength(100);
        });

        When(x => x.GenderSpecified && x.Gender is not null, () =>
        {
            RuleFor(x => x.Gender!).MaximumLength(32);
        });

        When(x => x.MobileNumberSpecified && x.MobileNumber is not null, () =>
        {
            RuleFor(x => x.MobileNumber!).MaximumLength(32);
        });

        When(x => x.PreferredLanguageSpecified && x.PreferredLanguage is not null, () =>
        {
            RuleFor(x => x.PreferredLanguage!).MaximumLength(16);
        });

        When(x => x.AddressSpecified && x.Address is not null, () =>
        {
            RuleFor(x => x.Address!).MaximumLength(500);
        });

        When(x => x.EmergencyContactSpecified && x.EmergencyContact is not null, () =>
        {
            RuleFor(x => x.EmergencyContact!).MaximumLength(250);
        });

        When(x => x.DateOfBirthSpecified && x.DateOfBirth.HasValue, () =>
        {
            RuleFor(x => x.DateOfBirth)
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Date of birth cannot be in the future.");
        });
    }
}

public sealed class RegisterPatientWithClinicRequestValidator : AbstractValidator<RegisterPatientWithClinicRequest>
{
    public RegisterPatientWithClinicRequestValidator()
    {
        RuleFor(x => x.ClinicCode)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Clinic code must be a lowercase slug.");
    }
}
