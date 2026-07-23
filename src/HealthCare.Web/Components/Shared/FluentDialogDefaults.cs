using Microsoft.FluentUI.AspNetCore.Components;

namespace HealthCare.Web.Components.Shared;

/// <summary>
/// Shared Fluent dialog parameter defaults (hide chrome actions when content supplies its own).
/// </summary>
public static class FluentDialogDefaults
{
    public static DialogParameters ContentOnly(string title, string? width = "480px") => new()
    {
        Title = title,
        Width = width,
        PrimaryAction = null,
        SecondaryAction = null,
        PreventDismissOnOverlayClick = true,
    };
}
