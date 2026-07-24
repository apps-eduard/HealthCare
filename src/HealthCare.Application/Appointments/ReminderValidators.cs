using FluentValidation;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;

namespace HealthCare.Application.Appointments;

public sealed class RetryAppointmentReminderRequestValidator : AbstractValidator<RetryAppointmentReminderRequest>
{
    public RetryAppointmentReminderRequestValidator()
    {
        RuleFor(x => x.ReminderId).NotEmpty();
    }
}

public sealed class StaffReminderSearchQueryValidator : AbstractValidator<StaffReminderSearchQuery>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public StaffReminderSearchQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<AppointmentReminderStatus>(s, ignoreCase: true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithErrorCode(AppointmentReminderErrorCodes.InvalidSearch)
            .WithMessage("Status must be a valid reminder status.");

        RuleFor(x => x)
            .Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc <= x.ToUtc)
            .WithErrorCode(AppointmentReminderErrorCodes.InvalidSearch)
            .WithMessage("FromUtc must be less than or equal to ToUtc.");
    }
}
