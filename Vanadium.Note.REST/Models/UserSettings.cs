using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public class UserSettings
{
    public Guid Id { get; set; }

    [MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DefaultSortBy { get; set; } = "date";

    [MaxLength(4)]
    public string DefaultSortDir { get; set; } = "desc";

    public int DefaultPageSize { get; set; } = 30;

    [MaxLength(6)]
    public string Theme { get; set; } = "system";
}
