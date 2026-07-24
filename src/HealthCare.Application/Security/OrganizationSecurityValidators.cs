using FluentValidation;
using HealthCare.Contracts.Security;

namespace HealthCare.Application.Security;

public sealed class OrganizationSecurityQueryValidator : AbstractValidator<OrganizationSecurityQuery>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxInclusiveDays = 93;

    public OrganizationSecurityQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(x => x.OrganizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationSecurityErrorCodes.InvalidScope);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationSecurityErrorCodes.ClinicNotFound);

        RuleFor(x => x.FromUtc)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateTimeOffset.TryParse(d, out _))
            .WithErrorCode(OrganizationSecurityErrorCodes.InvalidDateRange);

        RuleFor(x => x.ToUtc)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateTimeOffset.TryParse(d, out _))
            .WithErrorCode(OrganizationSecurityErrorCodes.InvalidDateRange);

        RuleFor(x => x)
            .Must(x =>
            {
                var hasFrom = !string.IsNullOrWhiteSpace(x.FromUtc);
                var hasTo = !string.IsNullOrWhiteSpace(x.ToUtc);
                if (hasFrom != hasTo)
                {
                    return false;
                }

                if (!hasFrom)
                {
                    return true;
                }

                if (!DateTimeOffset.TryParse(x.FromUtc, out var from)
                    || !DateTimeOffset.TryParse(x.ToUtc, out var to)
                    || from > to)
                {
                    return false;
                }

                return (to - from).TotalDays <= MaxInclusiveDays;
            })
            .WithErrorCode(OrganizationSecurityErrorCodes.InvalidDateRange)
            .WithMessage($"Provide both FromUtc and ToUtc within {MaxInclusiveDays} days.");
    }
}

public sealed class RevokeOrganizationSessionsRequestValidator : AbstractValidator<RevokeOrganizationSessionsRequest>
{
    public RevokeOrganizationSessionsRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(256);
    }
}

public sealed class CompromisedAccountResponseRequestValidator : AbstractValidator<CompromisedAccountResponseRequest>
{
    public CompromisedAccountResponseRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).MaximumLength(256);
    }
}
