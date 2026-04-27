using System.Text.Json.Serialization;

namespace Vanadium.Note.REST.Models;

public class LabelCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid UserId { get; set; }

    [JsonIgnore]
    public User? User { get; set; }

    public ICollection<Label> Labels { get; set; } = [];
}
