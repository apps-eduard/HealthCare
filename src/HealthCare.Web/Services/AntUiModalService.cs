using AntDesign;

namespace HealthCare.Web.Services;

public sealed class AntUiModalService(ModalService modalService, ConfirmService confirmService) : IUiModalService
{
    public Task<bool> ShowAsync<TComponent>(string title, string? width = null)
        where TComponent : FeedbackComponent<object?> =>
        ShowAsync<TComponent, object?>(title, null, width);

    public Task<bool> ShowAsync<TComponent, TContent>(string title, TContent content, string? width = null)
        where TComponent : FeedbackComponent<TContent>
    {
        var tcs = new TaskCompletionSource<bool>();
        var options = new ModalOptions
        {
            Title = title,
            Width = string.IsNullOrWhiteSpace(width) ? "480px" : width,
            Footer = null,
            DestroyOnClose = true,
            MaskClosable = false,
            Closable = true,
        };

        var modalRef = modalService.CreateModal<TComponent, TContent>(options, content);
        modalRef.OnOk = () =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        };
        modalRef.OnCancel = () =>
        {
            tcs.TrySetResult(false);
            return Task.CompletedTask;
        };
        modalRef.OnClose = () =>
        {
            tcs.TrySetResult(false);
            return Task.CompletedTask;
        };

        return tcs.Task;
    }

    public async Task<bool> ConfirmAsync(string title, string content)
    {
        var result = await confirmService.Show(content, title, ConfirmButtons.YesNo, ConfirmIcon.Warning);
        return result is ConfirmResult.Yes or ConfirmResult.OK;
    }
}
