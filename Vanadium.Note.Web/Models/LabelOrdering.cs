namespace Vanadium.Note.Web.Models;

/// <summary>
/// Shared display ordering for note-label chips (issue #186): category labels
/// first, grouped by category name, then general labels, sorted alphabetically
/// by name within each group — so category and general labels do not interleave.
/// </summary>
public static class LabelOrdering
{
    public static IOrderedEnumerable<Label> OrderForDisplay(this IEnumerable<Label> labels) =>
        labels
            .OrderBy(l => l.CategoryId.HasValue ? 0 : 1)
            .ThenBy(l => l.CategoryName)
            .ThenBy(l => l.Name);
}
