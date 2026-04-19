namespace Vanadium.Note.Web.Models;

public class LabelCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Label> Labels { get; set; } = [];
}
