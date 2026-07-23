using HealthCare.Web.Auth;

namespace HealthCare.Web.Appointments;

public enum AppointmentUiAction
{
    Confirm,
    CheckIn,
    Complete,
    NoShow,
    Cancel,
    Reschedule,
}

/// <summary>
/// Convenience visibility only — API authorization remains authoritative.
/// </summary>
public static class AppointmentActionRules
{
    public static IReadOnlyList<AppointmentUiAction> GetVisibleActions(
        string? status,
        IPermissionState permissions)
    {
        if (permissions is null || AppointmentStatusPresentation.IsTerminal(status))
        {
            return [];
        }

        var actions = new List<AppointmentUiAction>();

        switch (status)
        {
            case "Requested":
                TryAdd(actions, AppointmentUiAction.Confirm, permissions, WebPermissions.AppointmentsConfirm);
                TryAdd(actions, AppointmentUiAction.Cancel, permissions, WebPermissions.AppointmentsCancel);
                TryAdd(actions, AppointmentUiAction.Reschedule, permissions, WebPermissions.AppointmentsReschedule);
                break;

            case "Confirmed":
                TryAdd(actions, AppointmentUiAction.CheckIn, permissions, WebPermissions.AppointmentsCheckIn);
                TryAdd(actions, AppointmentUiAction.NoShow, permissions, WebPermissions.AppointmentsNoShow);
                TryAdd(actions, AppointmentUiAction.Cancel, permissions, WebPermissions.AppointmentsCancel);
                TryAdd(actions, AppointmentUiAction.Reschedule, permissions, WebPermissions.AppointmentsReschedule);
                break;

            case "CheckedIn":
                TryAdd(actions, AppointmentUiAction.Complete, permissions, WebPermissions.AppointmentsComplete);
                TryAdd(actions, AppointmentUiAction.NoShow, permissions, WebPermissions.AppointmentsNoShow);
                break;

            case "InProgress":
                TryAdd(actions, AppointmentUiAction.Complete, permissions, WebPermissions.AppointmentsComplete);
                break;
        }

        return actions;
    }

    public static bool CanShow(AppointmentUiAction action, string? status, IPermissionState permissions) =>
        GetVisibleActions(status, permissions).Contains(action);

    private static void TryAdd(
        List<AppointmentUiAction> actions,
        AppointmentUiAction action,
        IPermissionState permissions,
        string permission)
    {
        if (permissions.Has(permission))
        {
            actions.Add(action);
        }
    }
}
