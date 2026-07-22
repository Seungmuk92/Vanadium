using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Issue #271: the Board card-move persists a move with a SINGLE AddLabelToNoteAsync call
/// (no separate remove), relying on the server enforcing category mutual exclusion — adding a
/// category label removes any existing sibling label in the same category, atomically in one
/// SaveChanges. This characterizes that invariant so the client's single-call move stays correct:
/// the old column label is dropped, the new one added, and labels in other categories are kept.
/// </summary>
public class LabelMutualExclusionTests
{
    [Fact]
    public async Task AddLabel_SameCategorySibling_RemovesOldKeepsOtherCategories()
    {
        using var h = new TestHost();

        var status = await h.Labels.CreateCategoryAsync("Status");
        var todo = await h.Labels.CreateLabelAsync("Todo", status.Id);
        var done = await h.Labels.CreateLabelAsync("Done", status.Id);
        var general = await h.Labels.CreateLabelAsync("pinned", null);

        var note = await h.CreateNoteAsync();
        await h.Labels.AddLabelToNoteAsync(note.Id, todo.Id);
        await h.Labels.AddLabelToNoteAsync(note.Id, general.Id);

        // The Board move: add the sibling label in the same category with NO prior remove call.
        await h.Labels.AddLabelToNoteAsync(note.Id, done.Id);

        var loaded = await h.Notes.Get(note.Id);
        Assert.NotNull(loaded);
        var labelIds = loaded!.Labels.Select(l => l.Id).ToHashSet();

        Assert.Contains(done.Id, labelIds);        // moved-to column label is present
        Assert.DoesNotContain(todo.Id, labelIds);  // moved-from label auto-removed (mutual exclusion)
        Assert.Contains(general.Id, labelIds);      // an unrelated general label is untouched
    }
}
