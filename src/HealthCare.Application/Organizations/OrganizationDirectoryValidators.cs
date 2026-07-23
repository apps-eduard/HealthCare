using FluentValidation;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationSearchRequestValidator : AbstractValidator<OrganizationSearchRequest>
{
    public const int MaxPageSize = 100;

    private static readonly HashSet<string> AllowedSort =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "name", "slug", "createdatutc", "isactive", "cliniccount",
        };

    public OrganizationSearchRequestValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.SortBy)
            .Must(s => AllowedSort.Contains(s))
            .WithErrorCode(OrganizationErrorCodes.InvalidSort);
        RuleFor(x => x.SortDirection)
            .Must(d => d is "asc" or "desc" or "ASC" or "DESC")
            .WithErrorCode(OrganizationErrorCodes.InvalidSort);
    }
}
