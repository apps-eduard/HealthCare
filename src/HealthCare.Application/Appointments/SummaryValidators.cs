using FluentValidation;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;

namespace HealthCare.Application.Appointments;

public sealed class ClinicAppointmentSummaryQueryValidator : AbstractValidator<ClinicAppointmentSummaryQuery>
{
    public ClinicAppointmentSummaryQueryValidator()
    {
        RuleFor(x => x.Date)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithErrorCode(AppointmentSummaryErrorCodes.SummaryInvalidDate)
            .WithMessage("Date must be a valid calendar date (yyyy-MM-dd).");

        RuleFor(x => x.ClinicId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("ClinicId is invalid.");
    }
}

public sealed class ClinicAppointmentSummaryRunQueryValidator : AbstractValidator<ClinicAppointmentSummaryRunQuery>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public ClinicAppointmentSummaryRunQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<ClinicAppointmentSummaryRunStatus>(s, ignoreCase: true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithErrorCode(AppointmentSummaryErrorCodes.SummaryInvalidDate)
            .WithMessage("Status must be a valid summary run status.");

        RuleFor(x => x.FromDate)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithErrorCode(AppointmentSummaryErrorCodes.SummaryInvalidDate);

        RuleFor(x => x.ToDate)
            .Must(d => string.IsNullOrWhiteSpace(d) || DateOnly.TryParse(d, out _))
            .WithErrorCode(AppointmentSummaryErrorCodes.SummaryInvalidDate);

        RuleFor(x => x)
            .Must(x =>
            {
                if (string.IsNullOrWhiteSpace(x.FromDate) || string.IsNullOrWhiteSpace(x.ToDate))
                {
                    return true;
                }

                return DateOnly.TryParse(x.FromDate, out var from)
                       && DateOnly.TryParse(x.ToDate, out var to)
                       && from <= to;
            })
            .WithErrorCode(AppointmentSummaryErrorCodes.SummaryInvalidDate)
            .WithMessage("FromDate must be less than or equal to ToDate.");
    }
}
