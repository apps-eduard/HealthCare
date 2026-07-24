namespace HealthCare.Web.Services;

/// <summary>
/// UI toast notifications for staff pages (wraps Ant Design MessageService).
/// </summary>
public interface IUserNotificationService
{
    void Success(string message);

    void Error(string message);

    void Warning(string message);

    void Info(string message);
}
