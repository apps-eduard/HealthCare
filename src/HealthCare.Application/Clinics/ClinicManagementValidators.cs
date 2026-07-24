using System.Text.RegularExpressions;
using FluentValidation;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public static class ClinicSlugRules
{
    public static readonly Regex Pattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int MinLength = 2;

    public const int MaxLength = 100;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "login", "logout", "health", "swagger", "hangfire", "static", "assets",
    };

    public static string Normalize(string slug) => slug.Trim().ToLowerInvariant();

    public static bool IsValid(string slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length is >= MinLength and <= MaxLength
        && Pattern.IsMatch(slug)
        && !Reserved.Contains(slug);
}

public sealed class OrganizationClinicSearchRequestValidator : AbstractValidator<OrganizationClinicSearchRequest>
{
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSort =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "name", "slug", "specialty", "city", "createdatutc", "isactive",
        };

    public OrganizationClinicSearchRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(s => AllowedSort.Contains(s))
            .WithErrorCode(ClinicManagementErrorCodes.InvalidSort);
        RuleFor(x => x.SortDirection)
            .Must(d => d is "asc" or "desc" or "ASC" or "DESC")
            .WithErrorCode(ClinicManagementErrorCodes.InvalidSort);
    }
}

public sealed class CreateOrganizationClinicRequestValidator : AbstractValidator<CreateOrganizationClinicRequest>
{
    public CreateOrganizationClinicRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithErrorCode(ClinicManagementErrorCodes.NameRequired).MaximumLength(200);
        RuleFor(x => x.Slug)
            .NotEmpty().WithErrorCode(ClinicManagementErrorCodes.SlugRequired)
            .Must(s => ClinicSlugRules.IsValid(ClinicSlugRules.Normalize(s)))
            .WithErrorCode(ClinicManagementErrorCodes.SlugInvalid);
        RuleFor(x => x.Specialty).MaximumLength(150);
        RuleFor(x => x.PhoneNumber).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.AddressLine1).MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(30);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.TimeZoneId).NotEmpty().MaximumLength(64);
        When(x => x.InitialClinicAdmin is not null, () =>
        {
            RuleFor(x => x.InitialClinicAdmin!.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.InitialClinicAdmin!.FirstName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.InitialClinicAdmin!.LastName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.InitialClinicAdmin!.TemporaryPassword).NotEmpty().MinimumLength(10).MaximumLength(128);
            RuleFor(x => x.InitialClinicAdmin!.JobTitle).MaximumLength(150);
        });
    }
}

public sealed class UpdateOrganizationClinicRequestValidator : AbstractValidator<UpdateOrganizationClinicRequest>
{
    public UpdateOrganizationClinicRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x)
            .Must(HasAnyField)
            .WithErrorCode(ClinicManagementErrorCodes.EmptyUpdate)
            .WithMessage("At least one clinic field must be provided.");
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Name).NotEmpty().When(x => x.Name is not null)
            .WithErrorCode(ClinicManagementErrorCodes.NameRequired);
        RuleFor(x => x.Slug)
            .Must(s => s is null || ClinicSlugRules.IsValid(ClinicSlugRules.Normalize(s)))
            .WithErrorCode(ClinicManagementErrorCodes.SlugInvalid);
        RuleFor(x => x.Specialty).MaximumLength(150).When(x => x.Specialty is not null);
        RuleFor(x => x.PhoneNumber).MaximumLength(50).When(x => x.PhoneNumber is not null);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.AddressLine1).MaximumLength(200).When(x => x.AddressLine1 is not null);
        RuleFor(x => x.AddressLine2).MaximumLength(200).When(x => x.AddressLine2 is not null);
        RuleFor(x => x.City).MaximumLength(100).When(x => x.City is not null);
        RuleFor(x => x.Region).MaximumLength(100).When(x => x.Region is not null);
        RuleFor(x => x.PostalCode).MaximumLength(30).When(x => x.PostalCode is not null);
        RuleFor(x => x.Country).MaximumLength(100).When(x => x.Country is not null);
        RuleFor(x => x.TimeZoneId).MaximumLength(64).When(x => x.TimeZoneId is not null);
        RuleFor(x => x.TimeZoneId).NotEmpty().When(x => x.TimeZoneId is not null);
    }

    private static bool HasAnyField(UpdateOrganizationClinicRequest request) =>
        request.Name is not null
        || request.Slug is not null
        || request.Specialty is not null
        || request.PhoneNumber is not null
        || request.Email is not null
        || request.AddressLine1 is not null
        || request.AddressLine2 is not null
        || request.City is not null
        || request.Region is not null
        || request.PostalCode is not null
        || request.Country is not null
        || request.TimeZoneId is not null;
}

public sealed class ClinicActivationRequestValidator : AbstractValidator<ClinicActivationRequest>
{
    public ClinicActivationRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).MaximumLength(500)
            .WithErrorCode(ClinicManagementErrorCodes.InvalidReason);
    }
}
