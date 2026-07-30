using MudBlazor;
using Vanadium.Note.Web.Models;

namespace Vanadium.Note.Web.Components;

/// <summary>Maps a <see cref="PropertyType"/> to a distinct MudBlazor Material icon so property
/// pickers, panels, and the management page can show at a glance what type a property is (issue #343).</summary>
public static class PropertyTypeIcons
{
    public static string For(PropertyType type) => type switch
    {
        PropertyType.Text => Icons.Material.Filled.Notes,
        PropertyType.Number => Icons.Material.Filled.Tag,
        PropertyType.Select => Icons.Material.Filled.RadioButtonChecked,
        PropertyType.MultiSelect => Icons.Material.Filled.Checklist,
        PropertyType.Date => Icons.Material.Filled.CalendarToday,
        PropertyType.Checkbox => Icons.Material.Filled.CheckBox,
        _ => Icons.Material.Filled.Label
    };
}
