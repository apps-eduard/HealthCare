using AntDesign;

namespace HealthCare.Web.Services;

/// <summary>
/// Thin Ant Design modal helper (ModalService + ConfirmService).
/// </summary>
public interface IUiModalService
{
    /// <summary>
    /// Shows a modal whose content inherits <see cref="FeedbackComponent{TComponentOptions}"/> with no args.
    /// Returns true when the content confirms (Ok), false when cancelled/closed.
    /// </summary>
    Task<bool> ShowAsync<TComponent>(string title, string? width = null)
        where TComponent : FeedbackComponent<object?>;

    /// <summary>
    /// Shows a modal with typed content options (dialog Args).
    /// </summary>
    Task<bool> ShowAsync<TComponent, TContent>(string title, TContent content, string? width = null)
        where TComponent : FeedbackComponent<TContent>;

    /// <summary>
    /// Yes/No confirmation. Returns true for Yes/OK.
    /// </summary>
    Task<bool> ConfirmAsync(string title, string content);
}
