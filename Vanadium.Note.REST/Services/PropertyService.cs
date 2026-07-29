using Microsoft.EntityFrameworkCore;
using Vanadium.Note.REST.Data;
using Vanadium.Note.REST.Models;

namespace Vanadium.Note.REST.Services;

/// <summary>
/// Note Properties (issue #343): global property definitions + options + per-note typed values.
/// Mirrors the <see cref="LabelService"/> precedent (case-insensitive uniqueness enforced in the
/// service, archived-note 403, immediate saves that never touch <c>NoteItem.UpdatedAt</c>).
/// Definition-level scans use <c>IgnoreQueryFilters()</c> so recycle-bin notes' values are counted
/// and cleaned up (INV-P4). See docs/plannings/note-property/note-properties-feature.md.
/// </summary>
public class PropertyService(NoteDbContext db, ILogger<PropertyService> logger)
{
    public const int MaxDefinitions = 50;
    public const int MaxOptionsPerDefinition = 100;
    public const int MaxTextValueLength = 500;

    // ── Exceptions (mapped to status codes by the controller) ────────────────────

    /// <summary>Thrown when a value mutation targets an archived (read-only) note → 403.</summary>
    public class NoteArchivedException() : InvalidOperationException("Note is archived and read-only.");

    /// <summary>Case-insensitive duplicate definition/option name → 409.</summary>
    public class DuplicateNameException(string message) : InvalidOperationException(message);

    /// <summary>A cap (definition/option/text length) was exceeded → 400.</summary>
    public class CapExceededException(string message) : InvalidOperationException(message);

    /// <summary>Type change blocked because values exist (counted across all notes) → 409.</summary>
    public class TypeChangeBlockedException(int valueCount)
        : InvalidOperationException(
            $"Cannot change the type of a property that has values on {valueCount} note(s). Clear the values first.")
    {
        public int ValueCount { get; } = valueCount;
    }

    /// <summary>Invalid value payload, unknown/foreign option, or an option op on a non-Select
    /// definition → 400.</summary>
    public class PropertyValidationException(string message) : InvalidOperationException(message);

    // ── Definitions ──────────────────────────────────────────────────────────────

    public async Task<List<PropertyDefinitionDto>> GetAllAsync(bool includeUsage, CancellationToken ct = default)
    {
        var defs = await db.PropertyDefinitions
            .Include(d => d.Options)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToListAsync(ct);

        if (!includeUsage)
            return defs.Select(d => ToDto(d)).ToList();

        // Usage counts include recycle-bin AND archived notes (IgnoreQueryFilters): a count that
        // omitted recycle-bin values would let the user "safely" delete something a restore misses.
        var valueCounts = await db.NotePropertyValues.IgnoreQueryFilters()
            .GroupBy(v => v.DefinitionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var selectOptionCounts = await db.NotePropertyValues.IgnoreQueryFilters()
            .Where(v => v.SelectedOptionId != null)
            .GroupBy(v => v.SelectedOptionId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var multiOptionCounts = await db.NotePropertySelectedOptions.IgnoreQueryFilters()
            .GroupBy(s => s.OptionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return defs.Select(d => ToDto(
            d,
            valueCounts.GetValueOrDefault(d.Id),
            optId => selectOptionCounts.GetValueOrDefault(optId) + multiOptionCounts.GetValueOrDefault(optId)))
            .ToList();
    }

    public async Task<PropertyDefinitionDto> CreateAsync(string name, PropertyType type, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type))
            throw new PropertyValidationException($"Unknown property type '{(int)type}'.");

        name = name.Trim();
        if (name.Length == 0)
            throw new PropertyValidationException("Property name is required.");

        var lowered = name.ToLower();
        if (await db.PropertyDefinitions.AnyAsync(d => d.Name.ToLower() == lowered, ct))
            throw new DuplicateNameException($"A property named '{name}' already exists.");

        if (await db.PropertyDefinitions.CountAsync(ct) >= MaxDefinitions)
            throw new CapExceededException($"Cannot create more than {MaxDefinitions} properties.");

        var maxSort = await db.PropertyDefinitions.Select(d => (int?)d.SortOrder).MaxAsync(ct) ?? -1;
        var def = new PropertyDefinition { Name = name, Type = type, SortOrder = maxSort + 1 };
        db.PropertyDefinitions.Add(def);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Property definition created: {DefinitionId} (type {Type})", def.Id, type);
        return ToDto(def);
    }

    public async Task<PropertyDefinitionDto?> UpdateAsync(
        Guid id, string name, PropertyType type, int sortOrder, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type))
            throw new PropertyValidationException($"Unknown property type '{(int)type}'.");

        var def = await db.PropertyDefinitions.Include(d => d.Options).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return null;

        name = name.Trim();
        if (name.Length == 0)
            throw new PropertyValidationException("Property name is required.");

        if (!string.Equals(name, def.Name, StringComparison.OrdinalIgnoreCase))
        {
            var lowered = name.ToLower();
            if (await db.PropertyDefinitions.AnyAsync(d => d.Id != id && d.Name.ToLower() == lowered, ct))
                throw new DuplicateNameException($"A property named '{name}' already exists.");
        }

        if (type != def.Type)
        {
            // INV-P4: include recycle-bin values so a type change can't leave a mistyped value
            // hiding in the recycle bin to resurface (with the wrong column) on restore.
            var valueCount = await db.NotePropertyValues.IgnoreQueryFilters()
                .CountAsync(v => v.DefinitionId == id, ct);
            if (valueCount > 0)
                throw new TypeChangeBlockedException(valueCount);

            // Moving away from a Select kind makes options meaningless — drop them.
            if (def.Type is PropertyType.Select or PropertyType.MultiSelect
                && type is not (PropertyType.Select or PropertyType.MultiSelect))
                db.PropertyOptions.RemoveRange(def.Options);
        }

        def.Name = name;
        def.Type = type;
        def.SortOrder = sortOrder;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Property definition updated: {DefinitionId}", id);
        return ToDto(def);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var def = await db.PropertyDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null) return false;
        // DB cascade removes options → value rows → selection rows, INCLUDING soft-deleted and
        // archived notes' rows (cascades run below EF's query filters — no IgnoreQueryFilters sweep).
        db.PropertyDefinitions.Remove(def);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Property definition deleted: {DefinitionId}", id);
        return true;
    }

    // ── Options ──────────────────────────────────────────────────────────────────

    public async Task<PropertyOptionDto?> AddOptionAsync(Guid definitionId, string name, CancellationToken ct = default)
    {
        var def = await db.PropertyDefinitions.Include(d => d.Options).FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (def is null) return null;

        RequireSelectKind(def);

        name = name.Trim();
        if (name.Length == 0)
            throw new PropertyValidationException("Option name is required.");

        if (def.Options.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateNameException($"An option named '{name}' already exists on this property.");

        if (def.Options.Count >= MaxOptionsPerDefinition)
            throw new CapExceededException($"Cannot create more than {MaxOptionsPerDefinition} options per property.");

        var maxSort = def.Options.Count == 0 ? -1 : def.Options.Max(o => o.SortOrder);
        var option = new PropertyOption { DefinitionId = definitionId, Name = name, SortOrder = maxSort + 1 };
        db.PropertyOptions.Add(option);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Property option created: {OptionId} on definition {DefinitionId}", option.Id, definitionId);
        return ToDto(option);
    }

    public async Task<PropertyOptionDto?> UpdateOptionAsync(
        Guid definitionId, Guid optionId, string name, int sortOrder, CancellationToken ct = default)
    {
        var option = await db.PropertyOptions
            .FirstOrDefaultAsync(o => o.Id == optionId && o.DefinitionId == definitionId, ct);
        if (option is null) return null;

        name = name.Trim();
        if (name.Length == 0)
            throw new PropertyValidationException("Option name is required.");

        var lowered = name.ToLower();
        if (await db.PropertyOptions.AnyAsync(
                o => o.DefinitionId == definitionId && o.Id != optionId && o.Name.ToLower() == lowered, ct))
            throw new DuplicateNameException($"An option named '{name}' already exists on this property.");

        option.Name = name;
        option.SortOrder = sortOrder;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Property option updated: {OptionId}", optionId);
        return ToDto(option);
    }

    public async Task<bool> DeleteOptionAsync(Guid definitionId, Guid optionId, CancellationToken ct = default)
    {
        var option = await db.PropertyOptions
            .Include(o => o.Definition)
            .FirstOrDefaultAsync(o => o.Id == optionId && o.DefinitionId == definitionId, ct);
        if (option is null) return false;

        var isMultiSelect = option.Definition.Type == PropertyType.MultiSelect;
        db.PropertyOptions.Remove(option);
        // Cascades: Select value rows referencing it are deleted (composite FK); MultiSelect
        // selection rows are deleted — across all notes incl. soft-deleted/archived.
        await db.SaveChangesAsync(ct);

        if (isMultiSelect)
        {
            // INV-P1 cleanup: MultiSelect value rows left with zero selections must go, and must
            // include recycle-bin notes' rows, or a restored note resurrects a phantom "notempty".
            await db.NotePropertyValues.IgnoreQueryFilters()
                .Where(v => v.DefinitionId == definitionId && !v.SelectedOptions.Any())
                .ExecuteDeleteAsync(ct);
        }

        logger.LogInformation("Property option deleted: {OptionId} on definition {DefinitionId}", optionId, definitionId);
        return true;
    }

    // ── Note values ────────────────────────────────────────────────────────────────

    /// <summary>Upsert one value. Returns null when the note (recycle-bin/unknown) or definition is
    /// unknown → 404. Throws <see cref="NoteArchivedException"/> (403) or
    /// <see cref="PropertyValidationException"/> (400). A checkbox-false / empty-multiselect payload
    /// clears the value (INV-P1) and returns an empty DTO.</summary>
    public async Task<NotePropertyValueDto?> SetValueAsync(
        Guid noteId, Guid definitionId, SetNotePropertyValueRequest req, CancellationToken ct = default)
    {
        var note = await db.Notes
            .Where(n => n.Id == noteId)
            .Select(n => new { n.ArchivedAt })
            .FirstOrDefaultAsync(ct);              // global filter → recycle-bin note invisible → 404
        if (note is null) return null;
        if (note.ArchivedAt is not null) throw new NoteArchivedException();

        var def = await db.PropertyDefinitions.Include(d => d.Options).FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (def is null) return null;

        var (clearRequested, text, optionIds) = ValidatePayload(def, req);

        var row = await db.NotePropertyValues
            .Include(v => v.SelectedOptions)
            .FirstOrDefaultAsync(v => v.NoteId == noteId && v.DefinitionId == definitionId, ct);

        if (clearRequested)
        {
            if (row is not null)
            {
                db.NotePropertyValues.Remove(row);
                await db.SaveChangesAsync(ct);
            }
            logger.LogInformation("Property {DefinitionId} cleared on note {NoteId}", definitionId, noteId);
            return EmptyValueDto(def);
        }

        if (row is null)
        {
            row = new NotePropertyValue { NoteId = noteId, DefinitionId = definitionId };
            db.NotePropertyValues.Add(row);
        }

        // Null out ALL typed columns first, then set only the one for def.Type — heals any drift
        // from a prior type and keeps INV-P1.
        row.TextValue = null;
        row.NumberValue = null;
        row.DateValue = null;
        row.BoolValue = null;
        row.SelectedOptionId = null;

        switch (def.Type)
        {
            case PropertyType.Text:
                row.TextValue = text;
                break;
            case PropertyType.Number:
                row.NumberValue = req.NumberValue;
                break;
            case PropertyType.Date:
                row.DateValue = req.DateValue;
                break;
            case PropertyType.Checkbox:
                row.BoolValue = true;                 // only true is ever stored (INV-P1)
                break;
            case PropertyType.Select:
                row.SelectedOptionId = req.OptionId;
                break;
            case PropertyType.MultiSelect:
                var target = optionIds.ToHashSet();
                foreach (var s in row.SelectedOptions.Where(s => !target.Contains(s.OptionId)).ToList())
                    db.NotePropertySelectedOptions.Remove(s);
                var existing = row.SelectedOptions.Select(s => s.OptionId).ToHashSet();
                foreach (var oid in optionIds.Where(oid => !existing.Contains(oid)))
                    row.SelectedOptions.Add(new NotePropertySelectedOption
                    {
                        NoteId = noteId,
                        DefinitionId = definitionId,
                        OptionId = oid
                    });
                break;
        }

        await db.SaveChangesAsync(ct);             // NoteItem untouched — no UpdatedAt bump
        logger.LogInformation("Property {DefinitionId} set on note {NoteId}", definitionId, noteId);
        return BuildValueDto(def, row);
    }

    /// <summary>Clear one value (idempotent). Returns false when the note is unknown/recycle-bin
    /// (→ 404); throws <see cref="NoteArchivedException"/> (403). Missing value row → true (204).</summary>
    public async Task<bool> ClearValueAsync(Guid noteId, Guid definitionId, CancellationToken ct = default)
    {
        var note = await db.Notes
            .Where(n => n.Id == noteId)
            .Select(n => new { n.ArchivedAt })
            .FirstOrDefaultAsync(ct);
        if (note is null) return false;
        if (note.ArchivedAt is not null) throw new NoteArchivedException();

        var row = await db.NotePropertyValues
            .FirstOrDefaultAsync(v => v.NoteId == noteId && v.DefinitionId == definitionId, ct);
        if (row is not null)
        {
            db.NotePropertyValues.Remove(row);      // selections cascade
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Property {DefinitionId} cleared on note {NoteId}", definitionId, noteId);
        }
        return true;
    }

    // ── Validation / mapping helpers ─────────────────────────────────────────────

    private static void RequireSelectKind(PropertyDefinition def)
    {
        if (def.Type is not (PropertyType.Select or PropertyType.MultiSelect))
            throw new PropertyValidationException("Options can only be managed on Select or MultiSelect properties.");
    }

    /// <summary>Enforces the "exactly the matching member is set" rule (§7.2) and per-type checks.
    /// Returns whether the payload is a clear (checkbox-false / empty-multiselect), the trimmed text,
    /// and the distinct option ids for MultiSelect.</summary>
    private static (bool clear, string? text, List<Guid> optionIds) ValidatePayload(
        PropertyDefinition def, SetNotePropertyValueRequest req)
    {
        var textSet = req.TextValue is not null;
        var numberSet = req.NumberValue is not null;
        var dateSet = req.DateValue is not null;
        var boolSet = req.BoolValue is not null;
        var optionSet = req.OptionId is not null;
        var optionsSet = req.OptionIds is not null;

        void RejectForeignMembers(bool allowed)
        {
            // Every member except the allowed one must be null.
            var foreign =
                (textSet && def.Type != PropertyType.Text) ||
                (numberSet && def.Type != PropertyType.Number) ||
                (dateSet && def.Type != PropertyType.Date) ||
                (boolSet && def.Type != PropertyType.Checkbox) ||
                (optionSet && def.Type != PropertyType.Select) ||
                (optionsSet && def.Type != PropertyType.MultiSelect);
            if (foreign || !allowed)
                throw new PropertyValidationException($"Only the {def.Type} value member may be set.");
        }

        switch (def.Type)
        {
            case PropertyType.Text:
                RejectForeignMembers(textSet);
                var text = req.TextValue!.Trim();
                if (text.Length == 0)
                    throw new PropertyValidationException("Text value is empty; use DELETE to clear the value.");
                if (text.Length > MaxTextValueLength)
                    throw new PropertyValidationException($"Text value exceeds {MaxTextValueLength} characters.");
                return (false, text, []);

            case PropertyType.Number:
                RejectForeignMembers(numberSet);
                if (!double.IsFinite(req.NumberValue!.Value))
                    throw new PropertyValidationException("Number value must be finite.");
                return (false, null, []);

            case PropertyType.Date:
                RejectForeignMembers(dateSet);
                return (false, null, []);

            case PropertyType.Checkbox:
                RejectForeignMembers(boolSet);
                return (req.BoolValue == false, null, []);   // false normalizes to a clear (INV-P1)

            case PropertyType.Select:
                RejectForeignMembers(optionSet);
                if (!def.Options.Any(o => o.Id == req.OptionId!.Value))
                    throw new PropertyValidationException("Option does not belong to this property.");
                return (false, null, []);

            case PropertyType.MultiSelect:
                RejectForeignMembers(optionsSet);
                var distinct = req.OptionIds!.Distinct().ToList();
                if (distinct.Count == 0)
                    return (true, null, []);                 // empty list normalizes to a clear (INV-P1)
                if (distinct.Count != req.OptionIds!.Count)
                    throw new PropertyValidationException("Duplicate options are not allowed.");
                var valid = def.Options.Select(o => o.Id).ToHashSet();
                if (!distinct.All(valid.Contains))
                    throw new PropertyValidationException("One or more options do not belong to this property.");
                return (false, null, distinct);

            default:
                throw new PropertyValidationException($"Unknown property type '{(int)def.Type}'.");
        }
    }

    private static PropertyDefinitionDto ToDto(
        PropertyDefinition d, int? valueCount = null, Func<Guid, int>? optionNoteCount = null) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            SortOrder = d.SortOrder,
            ValueCount = valueCount,
            Options = d.Options
                .OrderBy(o => o.SortOrder)
                .ThenBy(o => o.Name)
                .Select(o => ToDto(o, optionNoteCount?.Invoke(o.Id)))
                .ToList()
        };

    private static PropertyOptionDto ToDto(PropertyOption o, int? noteCount = null) => new()
    {
        Id = o.Id,
        Name = o.Name,
        SortOrder = o.SortOrder,
        NoteCount = noteCount
    };

    private static NotePropertyValueDto BuildValueDto(PropertyDefinition def, NotePropertyValue row) => new()
    {
        DefinitionId = def.Id,
        Name = def.Name,
        Type = def.Type,
        TextValue = row.TextValue,
        NumberValue = row.NumberValue,
        DateValue = row.DateValue,
        BoolValue = row.BoolValue,
        OptionId = row.SelectedOptionId,
        OptionIds = row.SelectedOptions.Select(s => s.OptionId).ToList()
    };

    private static NotePropertyValueDto EmptyValueDto(PropertyDefinition def) => new()
    {
        DefinitionId = def.Id,
        Name = def.Name,
        Type = def.Type
    };
}
