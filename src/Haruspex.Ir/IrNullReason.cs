namespace Haruspex.Ir;

/// <param name="Rule">One of <see cref="IrNullRules"/>.</param>
/// <param name="At">What the rule applies to: a column (<c>schema.table.column</c>) or a source location.</param>
public sealed record IrNullReason(string Rule, string At);
