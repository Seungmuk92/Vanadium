namespace Vanadium.Note.Web.Models;

public class UserSettings
{
    public string DefaultSortBy { get; set; } = "date";
    public string DefaultSortDir { get; set; } = "desc";
    public int DefaultPageSize { get; set; } = 30;

    public string Theme { get; set; } = "system";
}
