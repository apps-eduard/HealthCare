using FluentValidation;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public sealed class ClinicSettingsQueryValidator : AbstractValidator<ClinicSettingsQuery>
{
    public ClinicSettingsQueryValidator()
    {
        RuleFor(x => x.ClinicId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("ClinicId must be a non-empty GUID when provided.");
    }
}

public sealed class UpdateClinicSettingsRequestValidator : AbstractValidator<UpdateClinicSettingsRequest>
{
    public UpdateClinicSettingsRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => x.HasAnyEditableField)
            .WithErrorCode(ClinicSettingsErrorCodes.EmptyUpdate)
            .WithMessage("At least one clinic profile field must be provided.");

        When(x => x.NameSpecified, () =>
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(200);
        });

        When(x => x.SpecialtySpecified && x.Specialty is not null, () =>
        {
            RuleFor(x => x.Specialty!).MaximumLength(150);
        });

        When(x => x.ContactEmailSpecified && x.ContactEmail is not null, () =>
        {
            RuleFor(x => x.ContactEmail!)
                .EmailAddress()
                .MaximumLength(256);
        });

        When(x => x.ContactPhoneSpecified && x.ContactPhone is not null, () =>
        {
            RuleFor(x => x.ContactPhone!).MaximumLength(50);
        });

        When(x => x.AddressSpecified && x.Address is not null, () =>
        {
            RuleFor(x => x.Address!).MaximumLength(200);
        });

        When(x => x.CitySpecified && x.City is not null, () =>
        {
            RuleFor(x => x.City!).MaximumLength(100);
        });

        When(x => x.CountrySpecified && x.Country is not null, () =>
        {
            RuleFor(x => x.Country!).MaximumLength(100);
        });

        When(x => x.DefaultTimeZoneIdSpecified, () =>
        {
            RuleFor(x => x.DefaultTimeZoneId)
                .NotEmpty()
                .MaximumLength(64);
        });
    }
}
