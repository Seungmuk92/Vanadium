using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

public class SettingsService(NoteDbContext db)
{
    private static readonly string[] AllowedSortBy = ["date", "title"];
    private static readonly string[] AllowedSortDir = ["asc", "desc"];
    private static readonly int[] AllowedPageSizes = [10, 20, 30, 50];
    private static readonly string[] AllowedThemes = ["system", "light", "dark"];

    public async Task<UserSettings> GetAsync(string username)
    {
        return await db.UserSettings.FirstOrDefaultAsync(s => s.Username == username)
            ?? new UserSettings { Username = username };
    }

    public async Task<UserSettings> UpsertAsync(string username, UserSettings incoming)
    {
        var sortBy = AllowedSortBy.Contains(incoming.DefaultSortBy) ? incoming.DefaultSortBy : "date";
        var sortDir = AllowedSortDir.Contains(incoming.DefaultSortDir) ? incoming.DefaultSortDir : "desc";
        var pageSize = AllowedPageSizes.Contains(incoming.DefaultPageSize) ? incoming.DefaultPageSize : 30;
        var theme = AllowedThemes.Contains(incoming.Theme) ? incoming.Theme : "system";

        var existing = await db.UserSettings.FirstOrDefaultAsync(s => s.Username == username);
        if (existing is null)
        {
            existing = new UserSettings { Id = Guid.NewGuid(), Username = username };
            db.UserSettings.Add(existing);
        }

        existing.DefaultSortBy = sortBy;
        existing.DefaultSortDir = sortDir;
        existing.DefaultPageSize = pageSize;
        existing.Theme = theme;

        await db.SaveChangesAsync();
        return existing;
    }
}
