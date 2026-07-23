using FluentValidation;
using HealthCare.Contracts.Appointments;

namespace HealthCare.Application.Appointments;

public sealed class RetryAppointmentReminderRequestValidator : AbstractValidator<RetryAppointmentReminderRequest>
{
    public RetryAppointmentReminderRequestValidator()
    {
        RuleFor(x => x.ReminderId).NotEmpty();
    }
}
