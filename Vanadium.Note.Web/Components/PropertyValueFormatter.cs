using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Components;

/// <summary>Renders a <see cref="NotePropertyValue"/> as the short display string used by list rows
/// and board cards (issue #343). Select/MultiSelect need the definition to resolve option ids to
/// names; a missing definition degrades to an empty string rather than leaking a raw GUID.</summary>
public static class PropertyValueFormatter
{
    public static string Format(PropertyDefinition? def, NotePropertyValue value) => value.Type switch
    {
        PropertyType.Text => value.TextValue ?? "",
        PropertyType.Number => value.NumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        PropertyType.Date => value.DateValue?.ToString("yyyy-MM-dd") ?? "",
        PropertyType.Checkbox => value.BoolValue == true ? "✓" : "",
        PropertyType.Select => def?.Options.FirstOrDefault(o => o.Id == value.OptionId)?.Name ?? "",
        PropertyType.MultiSelect => string.Join(", ",
            value.OptionIds.Select(oid => def?.Options.FirstOrDefault(o => o.Id == oid)?.Name).Where(n => n is not null)),
        _ => ""
    };
}
