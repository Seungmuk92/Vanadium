namespace Vanadium.Note.REST.Models;

public class Label
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public LabelCategory? Category { get; set; }
}
