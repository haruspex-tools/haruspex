namespace Haruspex.Ir;

/// <summary>
/// A table column a value is read from (0.25): what the value's type and nullability start from, before the rules that act on
/// it on the way to the result. So that a reader of the document alone (<c>haruspex explain</c>, an editor) can say why a field
/// is what it is, and whether that differs from what its column declares, without the SQL it was inferred from.
/// </summary>
/// <param name="Column">The column, as <c>schema.table.column</c>, written as a <see cref="IrNullRule.NullableColumn"/> reason names it.</param>
/// <param name="Sql">The column's type as the IR writes a scalar's (<see cref="IrScalar.Sql"/>), or null when it is not a scalar the IR types, such as a <c>json</c> column.</param>
/// <param name="At">Where the column is declared, as <c>path:line:column</c>.</param>
public sealed record IrSourceColumn(string Column, string? Sql, string At);
