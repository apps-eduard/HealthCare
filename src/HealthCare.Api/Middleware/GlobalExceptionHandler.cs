using System.Diagnostics;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Patients;
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
