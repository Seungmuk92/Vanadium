using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Service-level tests for the Quick Navigation feature (spec §11, T-1 .. T-9).
/// The trigram <c>EF.Functions.ILike</c> search path is PostgreSQL-only and cannot run on the
/// in-memory SQLite host, so term-matching scenarios (archive inclusion, Recycle Bin exclusion,
/// ordering, capping with matches, user scoping) are verified manually via Swagger/UI and marked
/// Skip here. Provider-agnostic logic — the empty-query guard and <c>BuildSnippet</c> — is tested directly.
/// </summary>
public class QuickNavServiceTests
{
    // ── T-5: empty / whitespace query never builds an ILike query ─────────────

    [Fact]
    public async Task QuickSearch_EmptyQuery_ReturnsEmpty()
    {
        using var h = new TestHost();
        await h.CreateNoteAsync("Anything");

        Assert.Empty(await h.Notes.QuickSearch("", 20));
        Assert.Empty(await h.Notes.QuickSearch("   ", 20));
    }

    // ── T-8: snippet around a mid-content match ──────────────────────────────

    [Fact]
    public void BuildSnippet_MatchMidContent_WindowedWithEllipses()
    {
        var content = new string('a', 60) + "oauth token here" + new string('b', 200);
        var snippet = NoteService.BuildSnippet(content, ["oauth"]);

        Assert.Contains("oauth", snippet, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("…", snippet);            // leading content was trimmed
        Assert.EndsWith("…", snippet);              // trailing content was trimmed
        Assert.True(snippet.Length <= 162);         // 160 chars + the two ellipsis marks
    }

    // ── T-9: title-only match / null / empty content ─────────────────────────

    [Fact]
    public void BuildSnippet_NoMatchInContent_FallsBackToLeadingSlice()
    {
        var content = "this content has no matching term at all";
        var snippet = NoteService.BuildSnippet(content, ["zzz"]);

        Assert.StartsWith("this content", snippet); // leading slice, no leading ellipsis
        Assert.False(snippet.StartsWith("…"));
    }

    [Fact]
    public void BuildSnippet_NullOrEmptyContent_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, NoteService.BuildSnippet(null, ["x"]));
        Assert.Equal(string.Empty, NoteService.BuildSnippet("", ["x"]));
    }

    [Fact]
    public void BuildSnippet_ShortContent_NoEllipses()
    {
        var snippet = NoteService.BuildSnippet("short body about oauth", ["oauth"]);

        Assert.Equal("short body about oauth", snippet);
    }

    // ── PG-only (trigram ILike) — verified manually via Swagger/UI ────────────

    [Fact(Skip = "QuickSearch uses EF.Functions.ILike (PostgreSQL trigram); verified manually via Swagger/UI.")]
    public Task QuickSearch_IncludesArchived_ExcludesRecycleBin_ScopedToUser_Clamped() => Task.CompletedTask;
}
