namespace Haruspex.Ir;

/// <param name="Rule">One of <see cref="IrNullRule"/>.</param>
/// <param name="At">What the rule applies to: a column (<c>schema.table.column</c>) or a source location.</param>
public sealed record IrNullReason(IrNullRule Rule, string At)
{
    /// <summary>
    /// What, at <see cref="At"/>, makes it so, in words, when there is more to say than the rule (0.29): for a value read from JSON
    /// at a path, the steps that may find nothing or null, <c>$.address.city: `$.address` may be left out, as the schema doesn't
    /// require it</c>. Null when the rule says it all.
    /// </summary>
    public string? Detail { get; init; }
}
