using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What the schema guarantees about a column's non-null values: its type's facets (length, digits, range) and what
/// its CHECK constraints prove (a value set, bounds, a minimum length). Every fact is something SQL Server enforces;
/// anything not proved is left out, which only leaves the type wider. None says anything about null: a CHECK passes
/// when its condition is UNKNOWN, so a constraint never rules null out (nullability is <see cref="SchemaRules.IsNeverNull(DatabaseModel, Table, Column)"/>'s).
/// </summary>
/// <param name="Type">The column's type with any alias resolved, or null when it cannot be resolved.</param>
/// <param name="Length">A string's length, from its type.</param>
/// <param name="MinLength">A string's minimum length, in UTF-16 code units, from a CHECK constraint.</param>
/// <param name="Digits">An exact number's precision and scale, from its type.</param>
/// <param name="Min">A number's lower bound: the tighter of its type's and its CHECK constraints'.</param>
/// <param name="Max">A number's upper bound, likewise.</param>
/// <param name="Values">A set the value is drawn from, from CHECK constraints.</param>
public sealed record ColumnFacts(
    ResolvedType? Type,
    LengthFact? Length = null,
    MinLengthFact? MinLength = null,
    DigitsFact? Digits = null,
    BoundFact? Min = null,
    BoundFact? Max = null,
    ValueSetFact? Values = null);
