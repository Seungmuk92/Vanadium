using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Note Properties (issue #343) value upsert/clear + read-model projection: all six kinds (T-2),
/// overwrite normalization (T-3), idempotent clear (T-4), checkbox false (T-5), multiselect replace
/// (T-6), payload validation (T-12), archive/recycle-bin/unknown guards (T-13), soft-delete round
/// trip (T-20).
/// </summary>
public class PropertyValueServiceTests
{
    // T-2
    [Fact]
    public async Task SetValue_AllSixKinds_ProjectedIntoNote_OrderedBySortOrder()
    {
        using var h = new TestHost();
        var text = await h.Properties.CreateAsync("Text", PropertyType.Text);
        var number = await h.Properties.CreateAsync("Number", PropertyType.Number);
        var select = await h.Properties.CreateAsync("Select", PropertyType.Select);
        var multi = await h.Properties.CreateAsync("Multi", PropertyType.MultiSelect);
        var date = await h.Properties.CreateAsync("Date", PropertyType.Date);
        var check = await h.Properties.CreateAsync("Check", PropertyType.Checkbox);
        var sOpt = await h.Properties.AddOptionAsync(select.Id, "S1");
        var m1 = await h.Properties.AddOptionAsync(multi.Id, "M1");
        var m2 = await h.Properties.AddOptionAsync(multi.Id, "M2");

        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, text.Id, new SetNotePropertyValueRequest { TextValue = "hello" });
        await h.Properties.SetValueAsync(note.Id, number.Id, new SetNotePropertyValueRequest { NumberValue = 42 });
        await h.Properties.SetValueAsync(note.Id, select.Id, new SetNotePropertyValueRequest { OptionId = sOpt!.Id });
        await h.Properties.SetValueAsync(note.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [m1!.Id, m2!.Id] });
        await h.Properties.SetValueAsync(note.Id, date.Id, new SetNotePropertyValueRequest { DateValue = new DateOnly(2026, 8, 1) });
        await h.Properties.SetValueAsync(note.Id, check.Id, new SetNotePropertyValueRequest { BoolValue = true });

        var loaded = await h.Notes.Get(note.Id);
        var props = loaded!.Properties;

        Assert.Equal(["Text", "Number", "Select", "Multi", "Date", "Check"], props.Select(p => p.Name));
        Assert.Equal("hello", props[0].TextValue);
        Assert.Equal(42, props[1].NumberValue);
        Assert.Equal(sOpt.Id, props[2].OptionId);
        Assert.Equal(new[] { m1.Id, m2.Id }.OrderBy(x => x), props[3].OptionIds.OrderBy(x => x));
        Assert.Equal(new DateOnly(2026, 8, 1), props[4].DateValue);
        Assert.True(props[5].BoolValue);
    }

    // T-3
    [Fact]
    public async Task Overwrite_SameDefinition_UpdatesSingleRow_NullsOtherColumns()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("N", PropertyType.Number);
        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 1 });
        await h.Properties.SetValueAsync(note.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 2 });

        var rows = await h.Db.NotePropertyValues.Where(v => v.DefinitionId == num.Id).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(2, rows[0].NumberValue);
        Assert.Null(rows[0].TextValue);
        Assert.Null(rows[0].BoolValue);
    }

    // T-4
    [Fact]
    public async Task Clear_RemovesRow_SecondClearStillSucceeds()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("N", PropertyType.Number);
        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 1 });

        Assert.True(await h.Properties.ClearValueAsync(note.Id, num.Id));
        Assert.Equal(0, await h.Db.NotePropertyValues.CountAsync());
        Assert.True(await h.Properties.ClearValueAsync(note.Id, num.Id));   // idempotent
    }

    // T-5
    [Fact]
    public async Task Checkbox_True_ThenFalse_DeletesRow()
    {
        using var h = new TestHost();
        var check = await h.Properties.CreateAsync("Done", PropertyType.Checkbox);
        var note = await h.CreateNoteAsync();

        await h.Properties.SetValueAsync(note.Id, check.Id, new SetNotePropertyValueRequest { BoolValue = true });
        Assert.Equal(1, await h.Db.NotePropertyValues.CountAsync());

        await h.Properties.SetValueAsync(note.Id, check.Id, new SetNotePropertyValueRequest { BoolValue = false });
        Assert.Equal(0, await h.Db.NotePropertyValues.CountAsync());
    }

    // T-6
    [Fact]
    public async Task MultiSelect_ReplaceThenEmpty_NormalizesSelections()
    {
        using var h = new TestHost();
        var multi = await h.Properties.CreateAsync("Tags", PropertyType.MultiSelect);
        var a = await h.Properties.AddOptionAsync(multi.Id, "A");
        var b = await h.Properties.AddOptionAsync(multi.Id, "B");
        var c = await h.Properties.AddOptionAsync(multi.Id, "C");
        var note = await h.CreateNoteAsync();

        await h.Properties.SetValueAsync(note.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [a!.Id, b!.Id] });
        await h.Properties.SetValueAsync(note.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [b.Id, c!.Id] });

        var selections = await h.Db.NotePropertySelectedOptions.Select(s => s.OptionId).ToListAsync();
        Assert.Equal(new[] { b.Id, c.Id }.OrderBy(x => x), selections.OrderBy(x => x));

        // Empty list clears the whole value row.
        await h.Properties.SetValueAsync(note.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [] });
        Assert.Equal(0, await h.Db.NotePropertyValues.CountAsync());
        Assert.Equal(0, await h.Db.NotePropertySelectedOptions.CountAsync());
    }

    // T-12
    [Fact]
    public async Task Validation_WrongTypedPayloads_Throw_NothingPersisted()
    {
        using var h = new TestHost();
        var number = await h.Properties.CreateAsync("N", PropertyType.Number);
        var select = await h.Properties.CreateAsync("S", PropertyType.Select);
        var other = await h.Properties.CreateAsync("Other", PropertyType.Select);
        var optOther = await h.Properties.AddOptionAsync(other.Id, "X");
        var note = await h.CreateNoteAsync();

        // Wrong member for the type (Text on a Number).
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, number.Id, new SetNotePropertyValueRequest { TextValue = "x" }));
        // Two members set.
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, number.Id,
                new SetNotePropertyValueRequest { NumberValue = 1, TextValue = "x" }));
        // Non-finite number.
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, number.Id,
                new SetNotePropertyValueRequest { NumberValue = double.NaN }));
        // Empty text (must use DELETE).
        var text = await h.Properties.CreateAsync("T", PropertyType.Text);
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, text.Id, new SetNotePropertyValueRequest { TextValue = "   " }));
        // Option from another definition.
        await Assert.ThrowsAsync<PropertyService.PropertyValidationException>(
            () => h.Properties.SetValueAsync(note.Id, select.Id,
                new SetNotePropertyValueRequest { OptionId = optOther!.Id }));

        Assert.Equal(0, await h.Db.NotePropertyValues.CountAsync());
    }

    // T-13
    [Fact]
    public async Task Guards_Archived403_RecycleBin404_UnknownDefinition404()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("N", PropertyType.Number);

        // Archived note → NoteArchivedException (403).
        var archived = await h.CreateNoteAsync();
        await h.Notes.Archive(archived.Id);
        await Assert.ThrowsAsync<PropertyService.NoteArchivedException>(
            () => h.Properties.SetValueAsync(archived.Id, def.Id, new SetNotePropertyValueRequest { NumberValue = 1 }));

        // Recycle-bin note → invisible → null (404).
        var deleted = await h.CreateNoteAsync();
        await h.Notes.Delete(deleted.Id);
        Assert.Null(await h.Properties.SetValueAsync(deleted.Id, def.Id, new SetNotePropertyValueRequest { NumberValue = 1 }));

        // Unknown definition on an active note → null (404).
        var active = await h.CreateNoteAsync();
        Assert.Null(await h.Properties.SetValueAsync(active.Id, Guid.NewGuid(), new SetNotePropertyValueRequest { NumberValue = 1 }));
    }

    // T-20
    [Fact]
    public async Task SoftDelete_HidesValuesFromFilters_RestoreBringsThemBack()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("Done", PropertyType.Checkbox);
        var note = await h.CreateNoteAsync();
        await h.Properties.SetValueAsync(note.Id, def.Id, new SetNotePropertyValueRequest { BoolValue = true });

        var filter = new List<PropertyFilter> { new(def.Id, PropertyFilterOp.Eq, "true") };

        var before = await h.Notes.GetPaged(1, 50, null, "date", "desc", filter);
        Assert.Contains(before.Items, n => n.Id == note.Id);

        await h.Notes.Delete(note.Id);
        var during = await h.Notes.GetPaged(1, 50, null, "date", "desc", filter);
        Assert.DoesNotContain(during.Items, n => n.Id == note.Id);

        await h.Notes.Restore(note.Id);
        var after = await h.Notes.GetPaged(1, 50, null, "date", "desc", filter);
        Assert.Contains(after.Items, n => n.Id == note.Id);
    }

    // T-13 (clear guards)
    [Fact]
    public async Task Clear_Archived403_RecycleBin404()
    {
        using var h = new TestHost();
        var def = await h.Properties.CreateAsync("N", PropertyType.Number);

        var archived = await h.CreateNoteAsync();
        await h.Notes.Archive(archived.Id);
        await Assert.ThrowsAsync<PropertyService.NoteArchivedException>(
            () => h.Properties.ClearValueAsync(archived.Id, def.Id));

        var deleted = await h.CreateNoteAsync();
        await h.Notes.Delete(deleted.Id);
        Assert.False(await h.Properties.ClearValueAsync(deleted.Id, def.Id));
    }
}
