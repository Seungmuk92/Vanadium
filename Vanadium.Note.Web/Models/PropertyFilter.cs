namespace Vanadium.Note.Web.Models;

public enum PropertyFilterOp
{
    Eq,
    Ne,
    Lt,
    Lte,
    Gt,
    Gte,
    Empty,
    NotEmpty,
    AnyOf
}

/// <summary>Client-side representation of one <c>pf</c> filter. Serializes to and parses from the
/// <c>{definitionId}:{op}[:{value}]</c> wire form used by the note list / board queries (§6.3).</summary>
public class PropertyFilter
{
    public Guid DefinitionId { get; set; }
    public PropertyFilterOp Op { get; set; }
    public string? Value { get; set; }

    /// <summary>The wire form: <c>{definitionId}:{op}[:{value}]</c> (value URL-encoded).</summary>
    public string ToQueryValue()
    {
        var op = OpToken(Op);
        return Op is PropertyFilterOp.Empty or PropertyFilterOp.NotEmpty || string.IsNullOrEmpty(Value)
            ? $"{DefinitionId}:{op}"
            : $"{DefinitionId}:{op}:{Uri.EscapeDataString(Value)}";
    }

    public static PropertyFilter? Parse(string raw)
    {
        var parts = raw.Split(':', 3);
        if (parts.Length < 2 || !Guid.TryParse(parts[0], out var id)) return null;
        var op = ParseOp(parts[1]);
        if (op is null) return null;
        var value = parts.Length == 3 ? Uri.UnescapeDataString(parts[2]) : null;
        return new PropertyFilter { DefinitionId = id, Op = op.Value, Value = value };
    }

    public static string OpToken(PropertyFilterOp op) => op switch
    {
        PropertyFilterOp.Eq => "eq",
        PropertyFilterOp.Ne => "ne",
        PropertyFilterOp.Lt => "lt",
        PropertyFilterOp.Lte => "lte",
        PropertyFilterOp.Gt => "gt",
        PropertyFilterOp.Gte => "gte",
        PropertyFilterOp.Empty => "empty",
        PropertyFilterOp.NotEmpty => "notempty",
        PropertyFilterOp.AnyOf => "anyof",
        _ => "eq"
    };

    private static PropertyFilterOp? ParseOp(string token) => token.ToLowerInvariant() switch
    {
        "eq" => PropertyFilterOp.Eq,
        "ne" => PropertyFilterOp.Ne,
        "lt" => PropertyFilterOp.Lt,
        "lte" => PropertyFilterOp.Lte,
        "gt" => PropertyFilterOp.Gt,
        "gte" => PropertyFilterOp.Gte,
        "empty" => PropertyFilterOp.Empty,
        "notempty" => PropertyFilterOp.NotEmpty,
        "anyof" => PropertyFilterOp.AnyOf,
        _ => null
    };
}
