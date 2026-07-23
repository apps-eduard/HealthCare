using FluentValidation;
using HealthCare.Contracts.Staff;
using HealthCare.Domain.Identity;

namespace HealthCare.Application.Staff;

public sealed class StaffSearchRequestValidator : AbstractValidator<StaffSearchRequest>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSort =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "lastname", "firstname", "email", "role", "createdatutc", "displayname",
        };

    public StaffSearchRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(s => AllowedSort.Contains(s))
            .WithErrorCode("staff.invalid_sort");
        RuleFor(x => x.SortDirection)
            .Must(d => d is "asc" or "desc" or "ASC" or "DESC")
            .WithErrorCode("staff.invalid_sort_direction");
        RuleFor(x => x.Role)
            .Must(r => string.IsNullOrWhiteSpace(r) || AppRoles.All.Contains(r!, StringComparer.Ordinal))
            .WithErrorCode("staff.invalid_role");
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).MaximumLength(200);
        RuleFor(x => x.JobTitle).MaximumLength(150);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TemporaryPassword)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(128);
    }
}

public sealed class UpdateStaffRequestValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FirstName).MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName).MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.DisplayName).MaximumLength(200).When(x => x.DisplayName is not null);
        RuleFor(x => x.JobTitle).MaximumLength(150).When(x => x.JobTitle is not null);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).When(x => x.PhoneNumber is not null);
        RuleFor(x => x)
            .Must(x => x.FirstName is not null
                       || x.LastName is not null
                       || x.DisplayName is not null
                       || x.JobTitle is not null
                       || x.PhoneNumber is not null)
            .WithErrorCode(StaffErrorCodes.EmptyPatch)
            .WithMessage("No editable staff fields were supplied.");
    }
}

public sealed class StaffActivationRequestValidator : AbstractValidator<StaffActivationRequest>
{
    public StaffActivationRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).MaximumLength(250);
    }
}
