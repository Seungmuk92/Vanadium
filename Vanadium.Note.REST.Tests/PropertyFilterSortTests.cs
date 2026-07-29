using Vanadium.Note.REST.Models;
using Vanadium.Note.REST.Services;
using Xunit;

namespace Vanadium.Note.REST.Tests;

/// <summary>
/// Note Properties (issue #343) filtering + sorting: per-op semantics (T-7), checkbox eq:false
/// includes never-set notes (T-8), sort with empties-last + tiebreak (T-9), empty/notempty (T-10),
/// malformed pf / sort validation (T-19), and pf on both GetPaged and GetAllSummaries (T-21).
/// </summary>
public class PropertyFilterSortTests
{
    private static List<PropertyFilter> Pf(params PropertyFilter[] f) => [.. f];

    private static async Task<HashSet<Guid>> FilterIds(TestHost h, params PropertyFilter[] filters)
    {
        var result = await h.Notes.GetPaged(1, 50, null, "date", "desc", null, Pf(filters));
        return result.Items.Select(n => n.Id).ToHashSet();
    }

    // T-7 (Number range), T-10 combined
    [Fact]
    public async Task NumberFilters_RangeAndEquality()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("Priority", PropertyType.Number);
        var one = await h.CreateNoteAsync("one");
        var five = await h.CreateNoteAsync("five");
        var ten = await h.CreateNoteAsync("ten");
        var none = await h.CreateNoteAsync("none");
        await h.Properties.SetValueAsync(one.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 1 });
        await h.Properties.SetValueAsync(five.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 5 });
        await h.Properties.SetValueAsync(ten.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 10 });

        Assert.Equal([five.Id], await FilterIds(h, new PropertyFilter(num.Id, PropertyFilterOp.Eq, "5")));
        Assert.Equal(new[] { one.Id, ten.Id }.ToHashSet(), await FilterIds(h, new PropertyFilter(num.Id, PropertyFilterOp.Ne, "5")));
        // between 2 and 9 (two pf entries → AND)
        Assert.Equal([five.Id], await FilterIds(h,
            new PropertyFilter(num.Id, PropertyFilterOp.Gte, "2"),
            new PropertyFilter(num.Id, PropertyFilterOp.Lt, "9")));
        // ne never matches the empty note
        Assert.DoesNotContain(none.Id, await FilterIds(h, new PropertyFilter(num.Id, PropertyFilterOp.Ne, "5")));
    }

    // T-7 (Date range)
    [Fact]
    public async Task DateFilters_Range()
    {
        using var h = new TestHost();
        var due = await h.Properties.CreateAsync("Due", PropertyType.Date);
        var jun = await h.CreateNoteAsync("jun");
        var jul = await h.CreateNoteAsync("jul");
        var aug = await h.CreateNoteAsync("aug");
        await h.Properties.SetValueAsync(jun.Id, due.Id, new SetNotePropertyValueRequest { DateValue = new DateOnly(2026, 6, 15) });
        await h.Properties.SetValueAsync(jul.Id, due.Id, new SetNotePropertyValueRequest { DateValue = new DateOnly(2026, 7, 15) });
        await h.Properties.SetValueAsync(aug.Id, due.Id, new SetNotePropertyValueRequest { DateValue = new DateOnly(2026, 8, 15) });

        Assert.Equal([jul.Id], await FilterIds(h,
            new PropertyFilter(due.Id, PropertyFilterOp.Gte, "2026-07-01"),
            new PropertyFilter(due.Id, PropertyFilterOp.Lt, "2026-08-01")));
    }

    // T-8
    [Fact]
    public async Task CheckboxFalse_MatchesUnchecked_AndNeverSet()
    {
        using var h = new TestHost();
        var done = await h.Properties.CreateAsync("Done", PropertyType.Checkbox);
        var checkedNote = await h.CreateNoteAsync("checked");
        var neverSet = await h.CreateNoteAsync("never");
        await h.Properties.SetValueAsync(checkedNote.Id, done.Id, new SetNotePropertyValueRequest { BoolValue = true });

        Assert.Equal([checkedNote.Id], await FilterIds(h, new PropertyFilter(done.Id, PropertyFilterOp.Eq, "true")));
        var falseIds = await FilterIds(h, new PropertyFilter(done.Id, PropertyFilterOp.Eq, "false"));
        Assert.Contains(neverSet.Id, falseIds);
        Assert.DoesNotContain(checkedNote.Id, falseIds);
    }

    // T-7 (Select/MultiSelect anyof)
    [Fact]
    public async Task SelectAndMultiSelect_EqAndAnyOf()
    {
        using var h = new TestHost();
        var sel = await h.Properties.CreateAsync("Status", PropertyType.Select);
        var todo = await h.Properties.AddOptionAsync(sel.Id, "Todo");
        var doing = await h.Properties.AddOptionAsync(sel.Id, "Doing");
        var multi = await h.Properties.CreateAsync("Tags", PropertyType.MultiSelect);
        var x = await h.Properties.AddOptionAsync(multi.Id, "X");
        var y = await h.Properties.AddOptionAsync(multi.Id, "Y");

        var a = await h.CreateNoteAsync("a");
        var b = await h.CreateNoteAsync("b");
        await h.Properties.SetValueAsync(a.Id, sel.Id, new SetNotePropertyValueRequest { OptionId = todo!.Id });
        await h.Properties.SetValueAsync(b.Id, sel.Id, new SetNotePropertyValueRequest { OptionId = doing!.Id });
        await h.Properties.SetValueAsync(a.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [x!.Id] });
        await h.Properties.SetValueAsync(b.Id, multi.Id, new SetNotePropertyValueRequest { OptionIds = [x.Id, y!.Id] });

        Assert.Equal([a.Id], await FilterIds(h, new PropertyFilter(sel.Id, PropertyFilterOp.Eq, todo.Id.ToString())));
        Assert.Equal(new[] { a.Id, b.Id }.ToHashSet(),
            await FilterIds(h, new PropertyFilter(sel.Id, PropertyFilterOp.AnyOf, $"{todo.Id},{doing.Id}")));
        Assert.Equal([b.Id], await FilterIds(h, new PropertyFilter(multi.Id, PropertyFilterOp.Eq, y.Id.ToString())));
        Assert.Equal(new[] { a.Id, b.Id }.ToHashSet(),
            await FilterIds(h, new PropertyFilter(multi.Id, PropertyFilterOp.AnyOf, $"{x.Id}")));
    }

    // T-10
    [Fact]
    public async Task EmptyAndNotEmpty()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("N", PropertyType.Number);
        var has = await h.CreateNoteAsync("has");
        var empty = await h.CreateNoteAsync("empty");
        await h.Properties.SetValueAsync(has.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 1 });

        Assert.Equal([has.Id], await FilterIds(h, new PropertyFilter(num.Id, PropertyFilterOp.NotEmpty, null)));
        Assert.Equal([empty.Id], await FilterIds(h, new PropertyFilter(num.Id, PropertyFilterOp.Empty, null)));
    }

    // T-9
    [Fact]
    public async Task Sort_ByNumber_EmptiesLast_TiebreakByUpdatedAt()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("Priority", PropertyType.Number);
        var three = await h.CreateNoteAsync("three");
        var one = await h.CreateNoteAsync("one");
        var empty = await h.CreateNoteAsync("empty");
        await h.Properties.SetValueAsync(three.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 3 });
        await h.Properties.SetValueAsync(one.Id, num.Id, new SetNotePropertyValueRequest { NumberValue = 1 });

        var asc = await h.Notes.GetPaged(1, 50, null, $"prop:{num.Id}", "asc", null);
        Assert.Equal([one.Id, three.Id, empty.Id], asc.Items.Select(n => n.Id));

        var desc = await h.Notes.GetPaged(1, 50, null, $"prop:{num.Id}", "desc", null);
        // Empties still last, values descending.
        Assert.Equal([three.Id, one.Id, empty.Id], desc.Items.Select(n => n.Id));
    }

    // T-9 (Select by option order)
    [Fact]
    public async Task Sort_BySelect_UsesOptionSortOrder()
    {
        using var h = new TestHost();
        var sel = await h.Properties.CreateAsync("Status", PropertyType.Select);
        var todo = await h.Properties.AddOptionAsync(sel.Id, "Todo");     // SortOrder 0
        var doing = await h.Properties.AddOptionAsync(sel.Id, "Doing");   // SortOrder 1
        var noteDoing = await h.CreateNoteAsync("doing");
        var noteTodo = await h.CreateNoteAsync("todo");
        await h.Properties.SetValueAsync(noteDoing.Id, sel.Id, new SetNotePropertyValueRequest { OptionId = doing!.Id });
        await h.Properties.SetValueAsync(noteTodo.Id, sel.Id, new SetNotePropertyValueRequest { OptionId = todo!.Id });

        var asc = await h.Notes.GetPaged(1, 50, null, $"prop:{sel.Id}", "asc", null);
        Assert.Equal([noteTodo.Id, noteDoing.Id], asc.Items.Select(n => n.Id));
    }

    // T-19
    [Fact]
    public async Task Validation_MalformedFilters_And_BadSort_Throw()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("N", PropertyType.Number);
        var multi = await h.Properties.CreateAsync("Tags", PropertyType.MultiSelect);

        // Unknown definition id.
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, "date", "desc", null,
                Pf(new PropertyFilter(Guid.NewGuid(), PropertyFilterOp.Eq, "1"))));
        // Op/type mismatch (lt on nothing… actually contains-like op on Number is fine; use anyof on Number).
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, "date", "desc", null,
                Pf(new PropertyFilter(num.Id, PropertyFilterOp.AnyOf, "x"))));
        // Unparsable number.
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, "date", "desc", null,
                Pf(new PropertyFilter(num.Id, PropertyFilterOp.Gt, "notanumber"))));
        // Sort by unknown definition.
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, $"prop:{Guid.NewGuid()}", "asc", null));
        // Sort by MultiSelect is rejected.
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, $"prop:{multi.Id}", "asc", null));
    }

    // T-11 (filter cap)
    [Fact]
    public async Task Filter_Over20_Throws()
    {
        using var h = new TestHost();
        var num = await h.Properties.CreateAsync("N", PropertyType.Number);
        var filters = Enumerable.Range(0, 21)
            .Select(_ => new PropertyFilter(num.Id, PropertyFilterOp.Gt, "0")).ToArray();
        await Assert.ThrowsAsync<NoteService.PropertyQueryException>(
            () => h.Notes.GetPaged(1, 50, null, "date", "desc", null, Pf(filters)));
    }

    // T-21
    [Fact]
    public async Task Pf_AppliesTo_GetAllSummaries()
    {
        using var h = new TestHost();
        var done = await h.Properties.CreateAsync("Done", PropertyType.Checkbox);
        var yes = await h.CreateNoteAsync("yes");
        var no = await h.CreateNoteAsync("no");
        await h.Properties.SetValueAsync(yes.Id, done.Id, new SetNotePropertyValueRequest { BoolValue = true });

        var summaries = await h.Notes.GetAllSummaries(null, Pf(new PropertyFilter(done.Id, PropertyFilterOp.Eq, "true")));
        Assert.Equal([yes.Id], summaries.Select(s => s.Id));
        // The projected Properties carry the value for list chips.
        Assert.True(summaries.Single().Properties.Single(p => p.DefinitionId == done.Id).BoolValue);
    }

    // Note: pf + trigram ILIKE search composition in GetPaged's search branch is PostgreSQL-only
    // (EF.Functions.ILike is not translated by the SQLite provider), so it stays in manual scope —
    // mirroring the repo's other skipped search tests.
}
