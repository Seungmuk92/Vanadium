using System.Text.Json.Serialization;

namespace Vanadium.Note.Web.Models;

/// <summary>Mirror of the REST <c>ShareMode</c> (see the REST project). Serialized as a string
/// so the two projects' enums stay in sync by name, not by ordinal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShareMode
{
    None = 0,
    Public = 1,
    Link = 2,
}
