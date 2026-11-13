using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A set the value is drawn from: numbers or strings.</summary>
/// <param name="Numbers">The numbers, sorted, when the column is numeric.</param>
/// <param name="Strings">
/// The strings, in the order the constraint lists them, when the column is a string; without trailing spaces, which
/// SQL Server ignores in comparing.
/// </param>
/// <param name="Comparison">How a value compares with them.</param>
/// <param name="From">The constraints that narrowed the set.</param>
public sealed record ValueSetFact(
    EquatableArray<ExactDecimal>? Numbers,
    EquatableArray<string>? Strings,
    ValueComparison Comparison,
    EquatableArray<FactSource> From);
