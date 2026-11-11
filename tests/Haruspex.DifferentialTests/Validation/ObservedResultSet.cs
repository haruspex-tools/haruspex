namespace Haruspex.DifferentialTests.Validation;

/// <summary>One result set as the driver returned it.</summary>
/// <param name="Columns">The column names, in order.</param>
/// <param name="Rows">Each row's values, in column order; SQL NULL is <c>null</c>, never <see cref="DBNull"/>.</param>
public sealed record ObservedResultSet(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);
