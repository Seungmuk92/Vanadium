using System.Net;
using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Regression coverage for issue #299: title-change propagation rewrites page-link/mention markup
/// with <c>Regex.Replace</c>, and <c>'$'</c> is a substitution metacharacter in the replacement
/// string (<c>$&amp;</c>, <c>$`</c>, <c>$1</c>, ...). <c>HtmlEncode</c> leaves <c>'$'</c> alone, so a
/// title such as "Cost is $100" or "Whole $&amp; match" corrupted every referencing note's link
/// before the fix. The propagated title must land in the markup verbatim.
/// </summary>
public class TitleDollarEscapingTests
{
    private static string PageLinkTo(Guid referencedId, string title) =>
        $"<div data-type=\"page-link\" data-note-id=\"{referencedId}\" data-title=\"{title}\" class=\"page-link-block\">" +
        $"<span class=\"page-link-icon\">📄</span><span class=\"page-link-title\">{title}</span></div>";

    private static string MentionTo(Guid referencedId, string title) =>
        $"<a data-type=\"note-mention\" data-note-id=\"{referencedId}\" data-title=\"{title}\" class=\"note-mention\">@{title}</a>";

    [Theory]
    [InlineData("Cost is $100")]      // AC example: $1/$10/$100 look like group references
    [InlineData("$1 reference")]      // leading numbered-group token
    [InlineData("Whole $& match")]    // $& = entire match — corrupts markup pre-fix
    [InlineData("Before $` after")]   // $` = text before match — corrupts markup pre-fix
    [InlineData("Double $$ sign")]    // $$ = literal $ — collapses pre-fix
    public async Task TitleWithDollar_PropagatesVerbatim_WithoutCorruption(string dollarTitle)
    {
        using var h = new TestHost();
        var target = await h.CreateNoteAsync("Placeholder");
        var referrer = await h.CreateNoteAsync("Referrer",
            content: PageLinkTo(target.Id, "Placeholder") + MentionTo(target.Id, "Placeholder"));

        var (_, conflict, _) = await h.Notes.Update(target.Id,
            new NoteItem { Title = dollarTitle, Content = target.Content, UpdatedAt = target.UpdatedAt });
        Assert.False(conflict);

        var fresh = await h.FindAsync(referrer.Id);
        var encoded = WebUtility.HtmlEncode(dollarTitle);

        // page-link: both the data-title attribute and the visible text carry the exact title.
        Assert.Contains($"data-title=\"{encoded}\"", fresh!.Content);
        Assert.Contains($"📄 {encoded}</div>", fresh.Content);
        // mention: visible text carries the exact title.
        Assert.Contains($"@{encoded}</a>", fresh.Content);
        // No substitution artifact: the pre-change title (which $& / $` would have injected) is gone,
        // and every reference was rewritten.
        Assert.DoesNotContain("Placeholder", fresh.Content);
    }
}
