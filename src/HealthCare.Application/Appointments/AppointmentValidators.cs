using FluentValidation;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;

namespace HealthCare.Application.Appointments;

public sealed class CreatePatientAppointmentRequestValidator : AbstractValidator<CreatePatientAppointmentRequest>
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;
    public const int MaxReasonLength = 500;
    public const int MaxNotesLength = 1000;

    public CreatePatientAppointmentRequestValidator()
    {
        RuleFor(x => x.ClinicCode)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$");

        RuleFor(x => x.DoctorStaffMemberId)
            .NotEmpty();

        RuleFor(x => x.AppointmentDateUtc)
            .Must(d => d > DateTimeOffset.UtcNow)
            .WithErrorCode(AppointmentErrorCodes.InvalidTime)
            .WithMessage("Appointment must be in the future.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(MinDurationMinutes, MaxDurationMinutes);

        RuleFor(x => x.Reason)
            .MaximumLength(MaxReasonLength)
            .When(x => x.Reason is not null);

        RuleFor(x => x.PatientNotes)
            .MaximumLength(MaxNotesLength)
            .When(x => x.PatientNotes is not null);
    }
}

public sealed class CreateStaffAppointmentRequestValidator : AbstractValidator<CreateStaffAppointmentRequest>
{
    public CreateStaffAppointmentRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.DoctorStaffMemberId).NotEmpty();

        RuleFor(x => x.AppointmentDateUtc)
            .Must(d => d > DateTimeOffset.UtcNow)
            .WithErrorCode(AppointmentErrorCodes.InvalidTime)
            .WithMessage("Appointment must be in the future.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(
                CreatePatientAppointmentRequestValidator.MinDurationMinutes,
                CreatePatientAppointmentRequestValidator.MaxDurationMinutes);

        RuleFor(x => x.Reason)
            .MaximumLength(CreatePatientAppointmentRequestValidator.MaxReasonLength)
            .When(x => x.Reason is not null);

        RuleFor(x => x.PatientNotes)
            .MaximumLength(CreatePatientAppointmentRequestValidator.MaxNotesLength)
            .When(x => x.PatientNotes is not null);

        RuleFor(x => x.ClinicId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("ClinicId must be a valid identifier when provided.");
    }
}

public sealed class AppointmentListQueryValidator : AbstractValidator<AppointmentListQuery>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "appointmentDateUtc",
        "status",
        "createdAtUtc",
        "durationMinutes",
    };

    public AppointmentListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);

        RuleFor(x => x.SortBy)
            .Must(s => AllowedSortFields.Contains(s))
            .WithMessage("Unsupported sort field.");

        RuleFor(x => x.SortDirection)
            .Must(d => d.Equals("asc", StringComparison.OrdinalIgnoreCase)
                       || d.Equals("desc", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<AppointmentStatus>(s, ignoreCase: true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Invalid appointment status filter.");

        RuleFor(x => x)
            .Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc <= x.ToUtc)
            .WithMessage("FromUtc must be less than or equal to ToUtc.");
    }
}

public sealed class AppointmentActionRequestValidator : AbstractValidator<AppointmentActionRequest>
{
    public AppointmentActionRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CancellationReason)
            .MaximumLength(500)
            .When(x => x.CancellationReason is not null);
    }
}

public sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public const int MaxRescheduleReasonLength = 250;

    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(x => x)
            .NotNull()
            .WithErrorCode(AppointmentErrorCodes.InvalidRequest)
            .WithMessage("Reschedule request is required.");

        RuleFor(x => x.ExpectedVersion)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(AppointmentErrorCodes.ConcurrencyConflict);

        RuleFor(x => x.AppointmentDateUtc)
            .Must(d => d > DateTimeOffset.UtcNow)
            .WithErrorCode(AppointmentErrorCodes.InvalidTime)
            .WithMessage("Appointment must be in the future.");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(
                CreatePatientAppointmentRequestValidator.MinDurationMinutes,
                CreatePatientAppointmentRequestValidator.MaxDurationMinutes)
            .WithErrorCode(AppointmentErrorCodes.InvalidRequest);

        RuleFor(x => x.DoctorStaffMemberId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithErrorCode(AppointmentErrorCodes.InvalidAssignedStaff)
            .WithMessage("Doctor staff member ID is invalid.");

        RuleFor(x => x.Reason)
            .MaximumLength(MaxRescheduleReasonLength)
            .When(x => x.Reason is not null);
    }
}
