using AntDesign;

namespace HealthCare.Web.Services;

public sealed class AntUserNotificationService(IMessageService messageService) : IUserNotificationService
{
    public void Success(string message) => messageService.Success(message);

    public void Error(string message) => messageService.Error(message);

    public void Warning(string message) => messageService.Warning(message);

    public void Info(string message) => messageService.Info(message);
}
