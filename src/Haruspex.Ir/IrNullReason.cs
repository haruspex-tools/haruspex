namespace Haruspex.Ir;

/// <param name="Rule">One of <see cref="IrNullRule"/>.</param>
/// <param name="At">What the rule applies to: a column (<c>schema.table.column</c>) or a source location.</param>
public sealed record IrNullReason(IrNullRule Rule, string At);
