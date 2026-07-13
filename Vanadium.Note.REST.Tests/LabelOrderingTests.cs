using Vanadium.Note.REST.Models;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Issue #186: note-label chips must not interleave category and general labels.
/// The service orders category labels first (grouped by category name), then
/// general labels, alphabetically by name within each group.
/// </summary>
public class LabelOrderingTests
{
    [Fact]
    public async Task Get_OrdersLabels_CategoryFirstThenGeneral_ByNameWithinGroup()
    {
        using var h = new TestHost();

        var category = await h.Labels.CreateCategoryAsync("Priority");
        // Names chosen so a pure name-sort would interleave the category label
        // ("mmm") between the two general labels ("aaa", "zzz").
        var catLabel = await h.Labels.CreateLabelAsync("mmm", category.Id);
        var generalZ = await h.Labels.CreateLabelAsync("zzz", null);
        var generalA = await h.Labels.CreateLabelAsync("aaa", null);

        var note = await h.CreateNoteAsync();
        // Add in an order that neither matches insertion nor pure name-sort output.
        await h.Labels.AddLabelToNoteAsync(note.Id, generalZ.Id);
        await h.Labels.AddLabelToNoteAsync(note.Id, catLabel.Id);
        await h.Labels.AddLabelToNoteAsync(note.Id, generalA.Id);

        var loaded = await h.Notes.Get(note.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "mmm", "aaa", "zzz" }, loaded!.Labels.Select(l => l.Name));
    }

    [Fact]
    public async Task GetPaged_OrdersLabels_CategoryFirstThenGeneral()
    {
        using var h = new TestHost();

        var category = await h.Labels.CreateCategoryAsync("Status");
        var catLabel = await h.Labels.CreateLabelAsync("mmm", category.Id);
        var generalZ = await h.Labels.CreateLabelAsync("zzz", null);
        var generalA = await h.Labels.CreateLabelAsync("aaa", null);

        var note = await h.CreateNoteAsync();
        await h.Labels.AddLabelToNoteAsync(note.Id, generalZ.Id);
        await h.Labels.AddLabelToNoteAsync(note.Id, catLabel.Id);
        await h.Labels.AddLabelToNoteAsync(note.Id, generalA.Id);

        var paged = await h.Notes.GetPaged(
            page: 1, pageSize: 10, search: null, sortBy: "date", sortDir: "desc", labelIds: null);

        var summary = Assert.Single(paged.Items);
        Assert.Equal(new[] { "mmm", "aaa", "zzz" }, summary.Labels.Select(l => l.Name));
    }
}
