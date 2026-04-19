namespace Vanadium.Note.REST.Models;

public class LabelCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ICollection<Label> Labels { get; set; } = [];
}
