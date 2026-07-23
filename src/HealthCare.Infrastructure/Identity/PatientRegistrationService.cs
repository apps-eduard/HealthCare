using System.Collections.Concurrent;
using HealthCare.Application.Identity;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Identity;

public sealed class PatientRegistrationService : IPatientRegistrationService
{
    private const string GenericRegistrationMessage =
        "If this email can be registered, a confirmation message has been sent.";

    private const string GenericResendMessage =
        "If this email requires confirmation, a confirmation message has been sent.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HealthCareDbContext _dbContext;
    private readonly IAccountEmailSender _emailSender;
    private readonly ILogger<PatientRegistrationService> _logger;

    public PatientRegistrationService(
        UserManager<ApplicationUser> userManager,
        HealthCareDbContext dbContext,
        IAccountEmailSender emailSender,
        ILogger<PatientRegistrationService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<PatientRegisterResponse> RegisterAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Registration submitted");

        var normalizedEmail = _userManager.NormalizeEmail(request.Email);
        var existing = await _userManager.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existing is not null)
        {
            // Do not reveal whether the email is already registered.
            _logger.LogInformation("Registration duplicate email handled generically");
            return new PatientRegisterResponse
            {
                Message = GenericRegistrationMessage,
                RequiresEmailConfirmation = true,
            };
        }

        var useTransaction = _dbContext.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (useTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email.Trim(),
                Email = request.Email.Trim(),
                EmailConfirmed = false,
                PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                    ? null
                    : request.PhoneNumber.Trim(),
                IsActive = true,
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                if (createResult.Errors.Any(e =>
                        e.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    _logger.LogInformation("Registration concurrent duplicate email handled generically");
                    return new PatientRegisterResponse
                    {
                        Message = GenericRegistrationMessage,
                        RequiresEmailConfirmation = true,
                    };
                }

                _logger.LogWarning(
                    "Registration failed. Codes={Codes}",
                    string.Join(',', createResult.Errors.Select(e => e.Code)));
                throw AuthenticationException.RegistrationFailed();
            }

            IdentityResult roleResult;
            try
            {
                roleResult = await _userManager.AddToRoleAsync(user, AppRoles.Patient);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Registration role assignment failed");
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    await _userManager.DeleteAsync(user);
                }

                throw AuthenticationException.RegistrationFailed();
            }

            if (!roleResult.Succeeded)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    await _userManager.DeleteAsync(user);
                }

                throw AuthenticationException.RegistrationFailed();
            }

            var patient = new Patient
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                DateOfBirth = request.DateOfBirth,
                MobileNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
                    ? null
                    : request.PhoneNumber.Trim(),
                IsActive = true,
            };

            _dbContext.Patients.Add(patient);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Patient account created. UserId={UserId} PatientId={PatientId}",
                user.Id,
                patient.Id);

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _emailSender.SendEmailConfirmationAsync(user.Email!, token, cancellationToken);

            return new PatientRegisterResponse
            {
                Message = GenericRegistrationMessage,
                RequiresEmailConfirmation = true,
            };
        }
        catch (AuthenticationException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogInformation("Registration concurrency conflict handled generically");
            return new PatientRegisterResponse
            {
                Message = GenericRegistrationMessage,
                RequiresEmailConfirmation = true,
            };
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<ConfirmEmailResponse> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await FindByEmailAsync(request.Email, cancellationToken);
        if (user is null)
        {
            _logger.LogInformation("Confirmation failed: unknown email");
            throw AuthenticationException.InvalidConfirmationToken();
        }

        if (await _userManager.IsEmailConfirmedAsync(user))
        {
            _logger.LogInformation("Confirmation completed for already-confirmed account. UserId={UserId}", user.Id);
            return new ConfirmEmailResponse
            {
                Message = "Email is already confirmed.",
                EmailConfirmed = true,
            };
        }

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            _logger.LogInformation("Confirmation failed. UserId={UserId}", user.Id);
            throw AuthenticationException.InvalidConfirmationToken();
        }

        _logger.LogInformation("Confirmation completed. UserId={UserId}", user.Id);
        return new ConfirmEmailResponse
        {
            Message = "Email confirmed successfully.",
            EmailConfirmed = true,
        };
    }

    public async Task<ResendConfirmationResponse> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || await _userManager.IsEmailConfirmedAsync(user))
        {
            return new ResendConfirmationResponse { Message = GenericResendMessage };
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailSender.SendEmailConfirmationAsync(user.Email!, token, cancellationToken);
        _logger.LogInformation("Confirmation email resent. UserId={UserId}", user.Id);

        return new ResendConfirmationResponse { Message = GenericResendMessage };
    }

    private async Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = _userManager.NormalizeEmail(email);
        return await _userManager.Users
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
    }
}

public sealed class DevelopmentConfirmationTokenStore : IDevelopmentConfirmationTokenStore
{
    private readonly ConcurrentDictionary<string, string> _tokens =
        new(StringComparer.OrdinalIgnoreCase);

    public void Store(string email, string token) =>
        _tokens[email.Trim()] = token;

    public bool TryGet(string email, out string? token) =>
        _tokens.TryGetValue(email.Trim(), out token);

    public void Clear(string email) =>
        _tokens.TryRemove(email.Trim(), out _);
}

public sealed class DevelopmentAccountEmailSender : IAccountEmailSender
{
    private readonly IDevelopmentConfirmationTokenStore _store;
    private readonly ILogger<DevelopmentAccountEmailSender> _logger;

    public DevelopmentAccountEmailSender(
        IDevelopmentConfirmationTokenStore store,
        ILogger<DevelopmentAccountEmailSender> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(
        string email,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        _store.Store(email, confirmationToken);
        // Never log the token or full confirmation link.
        _logger.LogInformation("Development confirmation token captured for email delivery simulation");
        return Task.CompletedTask;
    }
}
