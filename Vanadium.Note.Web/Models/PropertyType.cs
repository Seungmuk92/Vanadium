namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>PropertyType</c> enum (issue #343). Numeric values must match.</summary>
public enum PropertyType
{
    Text = 0,
    Number = 1,
    Select = 2,
    MultiSelect = 3,
    Date = 4,
    Checkbox = 5
}
