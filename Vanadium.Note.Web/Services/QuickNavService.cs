using Microsoft.JSInterop;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Services;

/// <summary>
/// Thin wrapper over the <c>window.quickNav</c> localStorage interop module.
/// Recents are a per-browser convenience list — never sent to the server.
/// </summary>
public sealed class QuickNavService(IJSRuntime js)
{
    public async Task<List<RecentNote>> GetRecentsAsync()
    {
        try
        {
            var recents = await js.InvokeAsync<List<RecentNote>>("quickNav.getRecents");
            return recents ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task PushRecentAsync(RecentNote entry)
    {
        try
        {
            await js.InvokeVoidAsync("quickNav.pushRecent", entry);
        }
        catch
        {
            /* best-effort: a failed Recents write must never break navigation */
        }
    }

    public async Task RemoveRecentAsync(Guid id)
    {
        try
        {
            await js.InvokeVoidAsync("quickNav.removeRecent", id);
        }
        catch
        {
            /* best-effort */
        }
    }
}
