namespace Vanadium.Note.REST.Models;

public class LabelSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}
