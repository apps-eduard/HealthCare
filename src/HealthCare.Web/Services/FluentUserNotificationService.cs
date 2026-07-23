using Microsoft.FluentUI.AspNetCore.Components;

namespace HealthCare.Web.Services;

public sealed class FluentUserNotificationService(IToastService toastService) : IUserNotificationService
{
    public void Success(string message) =>
        toastService.ShowSuccess(message);

    public void Error(string message) =>
        toastService.ShowError(message);

    public void Warning(string message) =>
        toastService.ShowWarning(message);

    public void Info(string message) =>
        toastService.ShowInfo(message);
}
