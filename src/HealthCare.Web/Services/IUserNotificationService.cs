namespace HealthCare.Web.Services;

/// <summary>
/// UI toast notifications for staff pages (wraps Fluent IToastService).
/// </summary>
public interface IUserNotificationService
{
    void Success(string message);

    void Error(string message);

    void Warning(string message);

    void Info(string message);
}
