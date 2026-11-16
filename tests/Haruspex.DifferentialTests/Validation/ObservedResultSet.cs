namespace Haruspex.DifferentialTests.Validation;

/// <summary>One result set as the driver returned it.</summary>
/// <param name="Columns">The column names, in order.</param>
/// <param name="Rows">Each row's values, in column order; SQL NULL is <c>null</c>, never <see cref="DBNull"/>.</param>
public sealed record ObservedResultSet(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)
{
    /// <summary>
    /// Each column's SQL type name as the driver reports it (<c>date</c>, <c>datetime2</c>, <c>decimal</c>), in column order; empty
    /// when the set was built by hand. Two types can arrive as one .NET type (a <c>date</c> and a <c>datetime2</c> are both a
    /// <see cref="DateTime"/>), and writing a row as JSON needs to tell them apart.
    /// </summary>
    public IReadOnlyList<string> ColumnTypes { get; init; } = [];
}
