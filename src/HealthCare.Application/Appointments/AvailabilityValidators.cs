using FluentValidation;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;

namespace HealthCare.Application.Appointments;

public sealed class CreateDoctorAvailabilityRequestValidator : AbstractValidator<CreateDoctorAvailabilityRequest>
{
    public CreateDoctorAvailabilityRequestValidator()
    {
        RuleFor(x => x.DayOfWeek)
            .Must(d => Enum.TryParse<DayOfWeek>(d, ignoreCase: true, out _))
            .WithMessage("DayOfWeek must be a valid weekday name.");

        RuleFor(x => x.StartLocalTime)
            .Must(BeTime)
            .WithMessage("StartLocalTime must be HH:mm.");

        RuleFor(x => x.EndLocalTime)
            .Must(BeTime)
            .WithMessage("EndLocalTime must be HH:mm.");

        RuleFor(x => x)
            .Must(x => ParseTime(x.StartLocalTime) < ParseTime(x.EndLocalTime))
            .WithMessage("StartLocalTime must be earlier than EndLocalTime.")
            .When(x => BeTime(x.StartLocalTime) && BeTime(x.EndLocalTime));

        RuleFor(x => x.SlotDurationMinutes)
            .Must(AvailabilitySlotRules.IsValidDuration)
            .WithErrorCode(AvailabilityErrorCodes.InvalidSlotDuration);

        RuleFor(x => x)
            .Must(x => !x.EffectiveTo.HasValue || x.EffectiveTo.Value >= x.EffectiveFrom)
            .WithMessage("EffectiveTo must be on or after EffectiveFrom.");
    }

    private static bool BeTime(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TimeOnly.TryParse(value, out _);

    private static TimeOnly ParseTime(string value) => TimeOnly.Parse(value);
}

public sealed class UpdateDoctorAvailabilityRequestValidator : AbstractValidator<UpdateDoctorAvailabilityRequest>
{
    public UpdateDoctorAvailabilityRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);

        When(x => x.StartLocalTime is not null, () =>
        {
            RuleFor(x => x.StartLocalTime!).Must(t => TimeOnly.TryParse(t, out _));
        });

        When(x => x.EndLocalTime is not null, () =>
        {
            RuleFor(x => x.EndLocalTime!).Must(t => TimeOnly.TryParse(t, out _));
        });

        When(x => x.SlotDurationMinutes.HasValue, () =>
        {
            RuleFor(x => x.SlotDurationMinutes!.Value)
                .Must(AvailabilitySlotRules.IsValidDuration)
                .WithErrorCode(AvailabilityErrorCodes.InvalidSlotDuration);
        });
    }
}

public sealed class CreateDoctorAvailabilityExceptionRequestValidator
    : AbstractValidator<CreateDoctorAvailabilityExceptionRequest>
{
    public CreateDoctorAvailabilityExceptionRequestValidator()
    {
        RuleFor(x => x.ExceptionType)
            .Must(t => Enum.TryParse<AvailabilityExceptionType>(t, ignoreCase: true, out _))
            .WithMessage("ExceptionType is invalid.");

        RuleFor(x => x.Reason).MaximumLength(250).When(x => x.Reason is not null);

        RuleFor(x => x)
            .Must(ValidRangeFields)
            .WithMessage("Exception time range is invalid for the selected type.");
    }

    private static bool ValidRangeFields(CreateDoctorAvailabilityExceptionRequest request)
    {
        if (!Enum.TryParse<AvailabilityExceptionType>(request.ExceptionType, ignoreCase: true, out var type))
        {
            return false;
        }

        if (type == AvailabilityExceptionType.UnavailableFullDay)
        {
            return request.StartLocalTime is null && request.EndLocalTime is null;
        }

        if (!TimeOnly.TryParse(request.StartLocalTime, out var start)
            || !TimeOnly.TryParse(request.EndLocalTime, out var end))
        {
            return false;
        }

        return start < end;
    }
}

public sealed class AvailableSlotsQueryValidator : AbstractValidator<AvailableSlotsQuery>
{
    public AvailableSlotsQueryValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
        When(x => x.DurationMinutes.HasValue, () =>
        {
            RuleFor(x => x.DurationMinutes!.Value)
                .Must(AvailabilitySlotRules.IsValidDuration)
                .WithErrorCode(AvailabilityErrorCodes.InvalidSlotDuration);
        });
    }
}
