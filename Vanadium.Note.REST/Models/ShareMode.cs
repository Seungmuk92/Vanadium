using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

/// <summary>
/// How a note is exposed to anonymous readers. Serialized as a string so the wire
/// contract survives reordering and is readable in logs/Swagger.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShareMode
{
    /// <summary>Not shared. The public share endpoint returns 404 for this note.</summary>
    None = 0,

    /// <summary>Anyone with the link can read it, and search engines may index it.</summary>
    Public = 1,

    /// <summary>Only someone with the link can read it; search-engine indexing is discouraged
    /// (<c>X-Robots-Tag: noindex</c>). Functionally identical to <see cref="Public"/> on the read path.</summary>
    Link = 2,
}
