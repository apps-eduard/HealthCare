using HealthCare.Api.Middleware;
using HealthCare.Application.Authorization;
using HealthCare.Application.DependencyInjection;
using HealthCare.Application.Identity;
using HealthCare.Infrastructure.DependencyInjection;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "HealthCare.Api")
        .WriteTo.Console());

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAppointmentReminders(builder.Configuration, builder.Environment);

    builder.Services.AddControllers();
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddOpenApi();

    builder.Services.AddHealthChecks()
        .AddDbContextCheck<HealthCareDbContext>("database", tags: ["ready", "live"]);

    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Instance = context.HttpContext.Request.Path,
            };

            var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] as string
                ?? context.HttpContext.TraceIdentifier;
            problem.Extensions["correlationId"] = correlationId;

            return new BadRequestObjectResult(problem);
        };
    });

    var app = builder.Build();

    await app.Services.SeedIdentityRolesAsync();
    await app.Services.SeedDevelopmentAdminAsync();
    await app.Services.SeedDevelopmentPatientAsync();

    app.UseExceptionHandler();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex is AuthenticationException or AuthorizationException)
            {
                return Serilog.Events.LogEventLevel.Information;
            }

            if (ex is not null || httpContext.Response.StatusCode >= 500)
            {
                return Serilog.Events.LogEventLevel.Error;
            }

            return httpContext.Response.StatusCode >= 400
                ? Serilog.Events.LogEventLevel.Warning
                : Serilog.Events.LogEventLevel.Information;
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "HealthCare API v1");
        });
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAppointmentReminderHangfire(app.Environment);
    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "HealthCare API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Expose Program for WebApplicationFactory integration tests.
public partial class Program;
