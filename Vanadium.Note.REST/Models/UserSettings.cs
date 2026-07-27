using System.ComponentModel.DataAnnotations;

namespace Vanadium.Note.REST.Models;

public class UserSettings
{
    public Guid Id { get; set; }

    [MaxLength(20)]
    public string DefaultSortBy { get; set; } = "date";

    [MaxLength(4)]
    public string DefaultSortDir { get; set; } = "desc";

    public int DefaultPageSize { get; set; } = 30;

    [MaxLength(6)]
    public string Theme { get; set; } = "system";

    /// <summary>
    /// Maintenance flag (issue #294): true once the one-time legacy content re-sanitize
    /// backfill has run. Not a user preference — it gates the startup backfill so notes
    /// stored before the persist-time sanitizer are cleaned exactly once, and never again.
    /// </summary>
    public bool LegacyContentSanitized { get; set; }
}
