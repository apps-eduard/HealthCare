using System.Diagnostics;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Patients;
using HealthCare.Application.Staff;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ItemKey] as string
            ?? httpContext.TraceIdentifier;

        if (exception is AuthenticationException authException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                authException.StatusCode,
                authException.Title,
                authException.ErrorCode,
                correlationId,
                "Authentication failed",
                cancellationToken);
        }

        if (exception is AuthorizationException authorizationException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                authorizationException.StatusCode,
                authorizationException.Title,
                authorizationException.ErrorCode,
                correlationId,
                "Authorization denied",
                cancellationToken);
        }

        if (exception is PatientLinkageException linkageException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                linkageException.Title,
                linkageException.ErrorCode,
                correlationId,
                "Patient linkage rejected",
                cancellationToken);
        }

        if (exception is PatientConcurrencyException concurrencyException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                concurrencyException.StatusCode,
                concurrencyException.Title,
                concurrencyException.ErrorCode,
                correlationId,
                "Patient concurrency conflict",
                cancellationToken);
        }

        if (exception is ClinicPatientConcurrencyException clinicPatientConcurrencyException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                clinicPatientConcurrencyException.StatusCode,
                clinicPatientConcurrencyException.Title,
                clinicPatientConcurrencyException.ErrorCode,
                correlationId,
                "Clinic patient concurrency conflict",
                cancellationToken);
        }

        if (exception is StaffManagementException staffException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                staffException.StatusCode,
                staffException.Title,
                staffException.ErrorCode,
                correlationId,
                "Staff management denied",
                cancellationToken);
        }

        if (exception is PatientClinicRegistrationException clinicRegistrationException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                clinicRegistrationException.StatusCode,
                clinicRegistrationException.Title,
                clinicRegistrationException.ErrorCode,
                correlationId,
                "Clinic registration denied",
                cancellationToken);
        }

        if (exception is AppointmentException appointmentException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                appointmentException.StatusCode,
                appointmentException.Title,
                appointmentException.ErrorCode,
                correlationId,
                "Appointment operation denied",
                cancellationToken);
        }

        if (exception is AvailabilityException availabilityException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                availabilityException.StatusCode,
                availabilityException.Title,
                availabilityException.ErrorCode,
                correlationId,
                "Availability operation denied",
                cancellationToken);
        }

        if (exception is AppointmentReminderException reminderException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                reminderException.StatusCode,
                reminderException.Title,
                reminderException.ErrorCode,
                correlationId,
                "Reminder operation denied",
                cancellationToken);
        }

        if (exception is AppointmentSummaryException summaryException)
        {
            return await WriteAuthProblemAsync(
                httpContext,
                summaryException.StatusCode,
                summaryException.Title,
                summaryException.ErrorCode,
                correlationId,
                "Appointment summary operation denied",
                cancellationToken);
        }

        _logger.LogError(
            exception,
            "Unhandled exception. CorrelationId={CorrelationId} Path={Path}",
            correlationId,
            httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://httpstatuses.com/500",
            Instance = httpContext.Request.Path,
            Detail = _environment.IsDevelopment()
                ? exception.Message
                : "An unexpected error occurred. Use the correlation ID when contacting support.",
        };

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = problem.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private async Task<bool> WriteAuthProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string title,
        string errorCode,
        string correlationId,
        string logMessage,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "{LogMessage}. Code={ErrorCode} CorrelationId={CorrelationId} Path={Path}",
            logMessage,
            errorCode,
            correlationId,
            httpContext.Request.Path);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = httpContext.Request.Path,
            Detail = title,
        };

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
