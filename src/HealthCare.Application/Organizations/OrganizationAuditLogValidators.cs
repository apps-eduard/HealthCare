using FluentValidation;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationAuditLogQueryValidator : AbstractValidator<OrganizationAuditLogQuery>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxInclusiveDays = 93;
    public const int MaxCorrelationIdLength = 64;
    public const int MaxActionLength = 128;
    public const int MaxCategoryLength = 64;
    public const int MaxResultCodeLength = 64;

    public OrganizationAuditLogQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(x => x.OrganizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationAuditLogErrorCodes.InvalidScope);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationAuditLogErrorCodes.ClinicNotFound);

        RuleFor(x => x.ActorUserId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationAuditLogErrorCodes.InvalidScope);

        RuleFor(x => x.Category).MaximumLength(MaxCategoryLength);
        RuleFor(x => x.Action).MaximumLength(MaxActionLength);
        RuleFor(x => x.ResultCode).MaximumLength(MaxResultCodeLength);
        RuleFor(x => x.CorrelationId).MaximumLength(MaxCorrelationIdLength);

        RuleFor(x => x.FromUtc)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateTimeOffset.TryParse(d, out _))
            .WithErrorCode(OrganizationAuditLogErrorCodes.InvalidDateRange);

        RuleFor(x => x.ToUtc)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateTimeOffset.TryParse(d, out _))
            .WithErrorCode(OrganizationAuditLogErrorCodes.InvalidDateRange);

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
            .WithErrorCode(OrganizationAuditLogErrorCodes.InvalidDateRange)
            .WithMessage($"Provide both FromUtc and ToUtc within {MaxInclusiveDays} days.");
    }
}

public sealed class OrganizationUsageQueryValidator : AbstractValidator<OrganizationUsageQuery>
{
    public OrganizationUsageQueryValidator()
    {
        RuleFor(x => x.OrganizationId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationUsageErrorCodes.InvalidScope);

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(OrganizationUsageErrorCodes.ClinicNotFound);
    }
}
