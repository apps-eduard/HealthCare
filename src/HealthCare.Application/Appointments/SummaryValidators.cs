using FluentValidation;
using HealthCare.Contracts.Appointments;

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
