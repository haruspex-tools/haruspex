using Haruspex.Collections;
using Haruspex.SqlServer.Semantics.Collations;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A set the value is drawn from: numbers or strings.</summary>
/// <param name="Numbers">The numbers, sorted, when the column is numeric.</param>
/// <param name="Strings">
/// The strings, in the order the constraint lists them, when the column is a string; without trailing spaces, which
/// SQL Server ignores in comparing. A <see cref="ValueComparison.Canonical"/> set's are in ordinal order instead, since an extracted
/// package keeps the server's normalised text of a constraint, which may list them in another order.
/// </param>
/// <param name="Comparison">How a value compares with them.</param>
/// <param name="From">The constraints that narrowed the set.</param>
/// <param name="Fold">The fold a <see cref="ValueComparison.Canonical"/> set compares by; null for any other.</param>
public sealed record ValueSetFact(
    EquatableArray<ExactDecimal>? Numbers,
    EquatableArray<string>? Strings,
    ValueComparison Comparison,
    EquatableArray<FactSource> From,
    CollationFold? Fold = null);
