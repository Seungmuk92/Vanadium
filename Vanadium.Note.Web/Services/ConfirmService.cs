using MudBlazor;
using Vanadium.Note.Web.Pages;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Shared wrapper around <see cref="ConfirmDialog"/> so pages don't each
/// re-implement the show-dialog-and-read-result dance (issue #124). Returns
/// <c>true</c> when the user confirms, <c>false</c> when they cancel or dismiss.
/// </summary>
public class ConfirmService(IDialogService dialogService)
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        Color confirmColor = Color.Error)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Message, message },
            { x => x.ConfirmText, confirmText },
            { x => x.ConfirmColor, confirmColor }
        };
        var dialog = await dialogService.ShowAsync<ConfirmDialog>(title, parameters);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }
}
