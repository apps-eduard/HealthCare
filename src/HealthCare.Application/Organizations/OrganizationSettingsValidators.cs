using FluentValidation;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationSettingsQueryValidator : AbstractValidator<OrganizationSettingsQuery>
{
    public OrganizationSettingsQueryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("OrganizationId must be a non-empty GUID when provided.");
    }
}

public sealed class UpdateOrganizationSettingsRequestValidator : AbstractValidator<UpdateOrganizationSettingsRequest>
{
    public UpdateOrganizationSettingsRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);

        RuleFor(x => x)
            .Must(x => x.HasAnyEditableField)
            .WithErrorCode(OrganizationSettingsErrorCodes.EmptyUpdate)
            .WithMessage("At least one organization profile field must be provided.");

        When(x => x.NameSpecified, () =>
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(200);
        });

        When(x => x.ContactEmailSpecified && x.ContactEmail is not null, () =>
        {
            RuleFor(x => x.ContactEmail!)
                .EmailAddress()
                .MaximumLength(256);
        });

        When(x => x.ContactPhoneSpecified && x.ContactPhone is not null, () =>
        {
            RuleFor(x => x.ContactPhone!).MaximumLength(32);
        });

        When(x => x.CountrySpecified && x.Country is not null, () =>
        {
            RuleFor(x => x.Country!).MaximumLength(100);
        });

        When(x => x.DefaultTimeZoneIdSpecified && x.DefaultTimeZoneId is not null, () =>
        {
            RuleFor(x => x.DefaultTimeZoneId!).MaximumLength(100);
        });

        When(x => x.BrandingPlaceholderSpecified && x.BrandingPlaceholder is not null, () =>
        {
            RuleFor(x => x.BrandingPlaceholder!).MaximumLength(200);
        });
    }
}
