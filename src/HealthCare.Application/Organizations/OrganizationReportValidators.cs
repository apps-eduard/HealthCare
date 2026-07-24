using FluentValidation;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationReportQueryValidator : AbstractValidator<OrganizationReportQuery>
{
    public const int MaxInclusiveDays = 93;

    public OrganizationReportQueryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationReportErrorCodes.InvalidScope)
            .WithMessage("OrganizationId is invalid.");

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationReportErrorCodes.ClinicNotFound)
            .WithMessage("ClinicId is invalid.");

        RuleFor(x => x.FromDate)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithErrorCode(OrganizationReportErrorCodes.InvalidDateRange)
            .WithMessage("FromDate must be a valid calendar date (yyyy-MM-dd).");

        RuleFor(x => x.ToDate)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithErrorCode(OrganizationReportErrorCodes.InvalidDateRange)
            .WithMessage("ToDate must be a valid calendar date (yyyy-MM-dd).");

        RuleFor(x => x)
            .Must(x =>
            {
                var hasFrom = !string.IsNullOrWhiteSpace(x.FromDate);
                var hasTo = !string.IsNullOrWhiteSpace(x.ToDate);
                if (hasFrom != hasTo)
                {
                    return false;
                }

                if (!hasFrom)
                {
                    return true;
                }

                if (!DateOnly.TryParse(x.FromDate, out var from) || !DateOnly.TryParse(x.ToDate, out var to))
                {
                    return false;
                }

                if (from > to)
                {
                    return false;
                }

                return to.DayNumber - from.DayNumber + 1 <= MaxInclusiveDays;
            })
            .WithErrorCode(OrganizationReportErrorCodes.InvalidDateRange)
            .WithMessage($"Provide both FromDate and ToDate, with FromDate ≤ ToDate, and at most {MaxInclusiveDays} inclusive days.");
    }
}
