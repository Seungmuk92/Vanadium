using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Note Properties (issue #343) definition/option lifecycle: creation ordering + usage counts (T-1),
/// caps (T-11), type-change guard incl. recycle-bin values (T-14), account wipe (T-16), duplicate
/// names (T-18), definition/option delete cascades (T-15, T-17).
/// </summary>
public class PropertyDefinitionServiceTests
{
    // T-1
    [Fact]
    public async Task GetAll_OrdersBySortOrder_UsageZeroInitially()
    {
        using var h = new TestHost();
        await h.Properties.CreateAsync("Priority", PropertyType.Number);
        await h.Properties.CreateAsync("Due", PropertyType.Date);
        var status = await h.Properties.CreateAsync("Status", PropertyType.Select);
        await h.Properties.AddOptionAsync(status.Id, "Todo");

        var defs = await h.Properties.GetAllAsync(includeUsage: true);

        Assert.Equal(["Priority", "Due", "Status"], defs.Select(d => d.Name));
        Assert.All(defs, d => Assert.Equal(0, d.ValueCount));
        var statusDto = defs.Single(d => d.Name == "Status");
        Assert.Equal(0, statusDto.Options.Single().NoteCount);
    }

    // T-18
    [Fact]
    public async Task Create_DuplicateName_CaseInsensitive_Throws()
    {
        using var h = new TestHost();
        await h.Properties.CreateAsync("Priority", PropertyType.Number);
        await Assert.ThrowsAsync<PropertyService.DuplicateNameException>(
            () => h.Properties.CreateAsync("priority", PropertyType.Text));
    }

    // T-18
    [Fact]
    public async Task AddOption_DuplicateName_CaseInsensitive_Throws()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Status", PropertyType.Select);
        await h.Properties.AddOptionAsync(def.Id, "Todo");
        await Assert.ThrowsAsync<PropertyService.DuplicateNameException>(
            () => h.Properties.AddOptionAsync(def.Id, "todo"));
    }

    // T-18
    [Fact]
    public async Task Rename_ToAnotherExistingName_Throws_ButSelfRenameOk()
    {
        using var h = new TestHost();
        var a = await h.Properties.CreateAsync("Priority", PropertyType.Number);
        await h.Properties.CreateAsync("Due", PropertyType.Date);

        await Assert.ThrowsAsync<PropertyService.DuplicateNameException>(
            () => h.Properties.UpdateAsync(a.Id, "due", PropertyType.Number, a.SortOrder));

        // Renaming to a case variant of itself is allowed.
        var updated = await h.Properties.UpdateAsync(a.Id, "PRIORITY", PropertyType.Number, a.SortOrder);
        Assert.Equal("PRIORITY", updated!.Name);
    }

    // T-11
    [Fact]
    public async Task Caps_Definitions_50thSucceeds_51stThrows()
    {
        using var h = new TestHost();

        for (var i = 0; i < PropertyService.MaxDefinitions; i++)
            await h.Properties.CreateAsync($"P{i}", PropertyType.Text);
        await Assert.ThrowsAsync<PropertyService.CapExceededException>(
            () => h.Properties.CreateAsync("overflow", PropertyType.Text));
    }

    // T-11 (option cap on its own host so the definition cap doesn't interfere)
    [Fact]
    public async Task Caps_OptionsPerDefinition_BoundarySucceeds_OverflowThrows()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Sel", PropertyType.MultiSelect);
        for (var i = 0; i < PropertyService.MaxOptionsPerDefinition; i++)
            await h.Properties.AddOptionAsync(def.Id, $"o{i}");
        await Assert.ThrowsAsync<PropertyService.CapExceededException>(
            () => h.Properties.AddOptionAsync(def.Id, "overflow"));
    }

    // T-11
    [Fact]
    public async Task Caps_TextValue_501Chars_Throws_500Ok()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Notes", PropertyType.Text);
        var note = await h.CreateNoteAsync();

        var ok = await h.Properties.SetValueAsync(note.Id, def.Id,
            new SetNotePropertyValueRequest { TextValue = new string('x', 500) });
        Assert.Equal(new string('x', 500), ok!.TextValue);

        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, def.Id,
                new SetNotePropertyValueRequest { TextValue = new string('x', 501) }));
    }

    // T-14
    [Fact]
    public async Task TypeChange_ZeroValues_Ok_WithActiveValue_Blocked_WithRecycleBinValue_Blocked()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Priority", PropertyType.Number);

        // Zero values: type change allowed.
        var changed = await h.Properties.UpdateAsync(def.Id, "Priority", PropertyType.Text, def.SortOrder);
        Assert.Equal(PropertyType.Text, changed!.Type);

        // Back to Number, then attach a value on an ACTIVE note → change blocked.
        await h.Properties.UpdateAsync(def.Id, "Priority", PropertyType.Number, def.SortOrder);
        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, def.Id, new SetNotePropertyValueRequest { NumberValue = 3 });
        await Assert.ThrowsAsync<PropertyService.TypeChangeBlockedException>(
            () => h.Properties.UpdateAsync(def.Id, "Priority", PropertyType.Text, def.SortOrder));

        // Move the note to the recycle bin — the value must STILL block the change (E6, IgnoreQueryFilters).
        await h.Notes.Delete(note.Id);
        await Assert.ThrowsAsync<PropertyService.TypeChangeBlockedException>(
            () => h.Properties.UpdateAsync(def.Id, "Priority", PropertyType.Text, def.SortOrder));
    }

    // T-15
    [Fact]
    public async Task DeleteDefinition_RemovesValuesEverywhere_RestoreLeavesNoOrphans()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Priority", PropertyType.Number);
        var active = await h.CreateNoteAsync("active");
        var deleted = await h.CreateNoteAsync("deleted");
        await h.Properties.SetValueAsync(active.Id, def.Id, new SetNotePropertyValueRequest { NumberValue = 1 });
        await h.Properties.SetValueAsync(deleted.Id, def.Id, new SetNotePropertyValueRequest { NumberValue = 2 });
        await h.Notes.Delete(deleted.Id);

        Assert.True(await h.Properties.DeleteAsync(def.Id));

        Assert.Equal(0, await h.Db.NotePropertyValues.IgnoreQueryFilters().CountAsync());
        await h.Notes.Restore(deleted.Id);
        var restored = await h.Notes.Get(deleted.Id);
        Assert.Empty(restored!.Properties);
    }

    // T-17
    [Fact]
    public async Task DeleteOption_RemovesSelectValues_CleansEmptyMultiSelectRows_IncludingRecycleBin()
    {
        using var h = new TestHost();
        var multi = await h.Properties.CreateAsync("Tags", PropertyType.MultiSelect);
        var a = await h.Properties.AddOptionAsync(multi.Id, "A");
        var b = await h.Properties.AddOptionAsync(multi.Id, "B");

        var keepsB = await h.CreateNoteAsync("keeps-b");   // {A, B} → loses A, keeps B (row survives)
        var onlyA = await h.CreateNoteAsync("only-a");     // {A}    → loses last option (row cleaned up)
        var binned = await h.CreateNoteAsync("binned");    // {A} on a recycle-bin note (E7)
        await h.Properties.SetValueAsync(keepsB.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [a!.Id, b!.Id] });
        await h.Properties.SetValueAsync(onlyA.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [a.Id] });
        await h.Properties.SetValueAsync(binned.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [a.Id] });
        await h.Notes.Delete(binned.Id);

        Assert.True(await h.Properties.DeleteOptionAsync(multi.Id, a.Id));

        // keepsB still has a value row (with B); onlyA's row is gone; binned's row is gone too.
        var keepsBValues = await h.Db.NotePropertyValues.IgnoreQueryFilters()
            .Include(v => v.SelectedOptions)
            .Where(v => v.DefinitionId == multi.Id).ToListAsync();
        Assert.Single(keepsBValues);
        Assert.Equal(keepsB.Id, keepsBValues[0].NoteId);
        Assert.Equal(b.Id, keepsBValues[0].SelectedOptions.Single().OptionId);
    }

    // T-16
    [Fact]
    public async Task AccountWipe_RemovesAllPropertyTables()
    {
        using var h = new TestHost();
        var multi = await h.Properties.CreateAsync("Tags", PropertyType.MultiSelect);
        var a = await h.Properties.AddOptionAsync(multi.Id, "A");
        var num = await h.Properties.CreateAsync("Priority", PropertyType.Number);
        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [a!.Id] });
        await h.Properties.SetValueAsync(note.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 5 });

        await h.Account.PurgeAllDataAsync();

        Assert.Equal(0, await h.Db.PropertyDefinitions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await h.Db.PropertyOptions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await h.Db.NotePropertyValues.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await h.Db.NotePropertySelectedOptions.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AddOption_OnNonSelectDefinition_Throws()
    {
        using var h = new TestHost();
        var text = await h.Properties.CreateAsync("Notes", PropertyType.Text);
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.AddOptionAsync(text.Id, "nope"));
    }

    [Fact]
    public async Task TypeChange_AwayFromSelect_DropsOptions()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Status", PropertyType.Select);
        await h.Properties.AddOptionAsync(def.Id, "Todo");
        await h.Properties.UpdateAsync(def.Id, "Status", PropertyType.Text, def.SortOrder);
        Assert.Equal(0, await h.Db.PropertyOptions.CountAsync());
    }
}
