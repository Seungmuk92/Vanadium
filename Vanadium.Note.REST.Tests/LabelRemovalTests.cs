using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Guards the label-feature removal (issue #373). The label entities, their tables and the
/// <c>labelIds</c> query path are gone; these assertions fail if any of them is reintroduced by a
/// partial revert or a stray merge, which would silently resurrect the dropped schema.
/// </summary>
public class LabelRemovalTests
{
    private static readonly string[] LabelTables = ["Labels", "LabelCategories", "NoteLabels"];

    [Fact]
    public void Model_HasNoLabelEntityTypes()
    {
        using var h = new TestHost();

        var entityNames = h.Db.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.DoesNotContain("Label", entityNames);
        Assert.DoesNotContain("LabelCategory", entityNames);
        Assert.DoesNotContain("NoteLabel", entityNames);
    }

    [Fact]
    public void Model_MapsNoLabelTables()
    {
        using var h = new TestHost();

        var tableNames = h.Db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null)
            .ToList();

        foreach (var table in LabelTables)
            Assert.DoesNotContain(table, tableNames);
    }

    /// <summary>The schema EnsureCreated builds from the current model must not contain the dropped
    /// tables — the SQLite counterpart of the PostgreSQL <c>RemoveLabels</c> drop migration.</summary>
    [Fact]
    public async Task CreatedSchema_HasNoLabelTables()
    {
        using var h = new TestHost();

        var existing = new List<string>();
        var connection = h.Db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            existing.Add(reader.GetString(0));

        foreach (var table in LabelTables)
            Assert.DoesNotContain(table, existing);
    }

    /// <summary>A note's own content is untouched by the removal: the drop migration only removes the
    /// three label tables, so title/content round-trip exactly as before (issue #373 AC).</summary>
    [Fact]
    public async Task NoteContent_SurvivesWithoutLabels()
    {
        using var h = new TestHost();
        var note = await h.CreateNoteAsync("Kept title", content: "<p>Kept body</p>");

        var fetched = await h.Notes.Get(note.Id);

        Assert.NotNull(fetched);
        Assert.Equal("Kept title", fetched!.Title);
        Assert.Equal("<p>Kept body</p>", fetched.Content);
    }
}
