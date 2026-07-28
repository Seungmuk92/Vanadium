using Bunit;
using Vanadium.Note.Web.Components;
using Xunit;

namespace Vanadium.Note.Web.Tests.Components;

/// <summary>
/// bUnit smoke test for the shared <see cref="Pagination"/> component (issue #308).
/// Establishes that the Web project can render a real Blazor component in-process and
/// that the pager's core contract (hide on single page, emit the selected page) holds.
/// </summary>
public sealed class PaginationSmokeTests : TestContext
{
    [Fact]
    public void RendersNothing_WhenSinglePage()
    {
        var cut = RenderComponent<Pagination>(parameters => parameters
            .Add(p => p.CurrentPage, 1)
            .Add(p => p.TotalPages, 1)
            .Add(p => p.InfoText, "1-1 of 1")
            .Add(p => p.OnPageChanged, _ => { }));

        // The component deliberately renders empty markup when there is only one page,
        // so callers no longer need their own guard.
        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void RendersPageButtons_WhenMultiplePages()
    {
        var cut = RenderComponent<Pagination>(parameters => parameters
            .Add(p => p.CurrentPage, 2)
            .Add(p => p.TotalPages, 5)
            .Add(p => p.InfoText, "31-60 of 150")
            .Add(p => p.OnPageChanged, _ => { }));

        Assert.Contains("pagination-bar", cut.Markup);
        // Active page marker is applied to the current page only.
        var active = cut.FindAll(".page-btn-active");
        Assert.Single(active);
        Assert.Equal("2", active[0].TextContent.Trim());
    }

    [Fact]
    public void RaisesOnPageChanged_WithTargetPage()
    {
        int? changedTo = null;
        var cut = RenderComponent<Pagination>(parameters => parameters
            .Add(p => p.CurrentPage, 2)
            .Add(p => p.TotalPages, 5)
            .Add(p => p.InfoText, "31-60 of 150")
            .Add(p => p.OnPageChanged, page => changedTo = page));

        // The "»" last-page control jumps to TotalPages.
        cut.FindAll("button.page-btn").Last().Click();

        Assert.Equal(5, changedTo);
    }
}
