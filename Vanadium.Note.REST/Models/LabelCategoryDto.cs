namespace Vanadium.Note.REST.Models;

public class LabelCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<LabelSummary> Labels { get; set; } = [];
}
