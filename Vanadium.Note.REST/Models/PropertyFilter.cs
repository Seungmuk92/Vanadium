namespace Vanadium.Note.REST.Models;

/// <summary>Operators supported by the <c>pf</c> property-filter grammar (§6.3). Which ops are
/// valid depends on the definition's <see cref="PropertyType"/>; the service rejects invalid
/// combinations with a 400.</summary>
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

/// <summary>One parsed <c>pf={definitionId}:{op}[:{value}]</c> entry. The controller parses the raw
/// query strings into these; the service resolves definitions, validates op/type/value, and builds
/// the EXISTS subqueries.</summary>
public record PropertyFilter(Guid DefinitionId, PropertyFilterOp Op, string? RawValue);
