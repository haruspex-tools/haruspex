namespace Haruspex.Ir;

/// <param name="Name">The parameter's name as declared, including any sigil such as <c>@</c>.</param>
/// <param name="Type">
/// The declared type, with its facts: what the parameter keeps of a value a caller sends, and so the rule a client checks a
/// value against before sending it (a longer string is cut short, a decimal of more places rounded).
/// </param>
/// <param name="HasDefault">Whether the caller may omit it.</param>
/// <param name="IsOutput">Whether it is an output parameter.</param>
public sealed record IrParameter(string Name, IrType Type, bool HasDefault, bool IsOutput)
{
    /// <summary>
    /// For a table-valued parameter whose type the document defines (0.26), the key of its definition in
    /// <see cref="IrDocument.TableTypes"/>; null for every other parameter.
    /// </summary>
    public string? TableType { get; init; }

    /// <summary>
    /// The database's description of the parameter (0.33), from an extended property the config file names; null when it has none. Documentation, not a claim, so
    /// <see cref="IrComparison"/> reports no change in it. Why: docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md.
    /// </summary>
    public string? Description { get; init; }
}
