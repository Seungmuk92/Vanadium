namespace Vanadium.Note.REST.Models;

/// <summary>The six v1 property value types. Numeric values are stable across the
/// wire and DB, so new types must be appended (never re-numbered).</summary>
public enum PropertyType
{
    Text = 0,
    Number = 1,
    Select = 2,
    MultiSelect = 3,
    Date = 4,
    Checkbox = 5
}
