using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The shapes inference works with. They are the IR's types before naming: objects are anonymous here, and
// unknowns carry their own location. IrBuilder turns them into the IR.

internal abstract record Shape;

internal sealed record ScalarShape(IrScalar Type) : Shape;

/// <param name="ItemsNullableBecause">Why an item can be null (only under <c>NULL ON NULL</c>); null or empty when never.</param>
/// <param name="MinItems">The fewest items, when more than none is proved.</param>
/// <param name="MaxItems">The most items, when bounded.</param>
internal sealed record ArrayShape(Shape Items, IReadOnlyList<IrNullReason>? ItemsNullableBecause = null, int? MinItems = null, int? MaxItems = null) : Shape;

/// <summary>A fixed-length array, each position with its own type: what <c>json_array</c> builds.</summary>
internal sealed record TupleShape(IReadOnlyList<ElementShape> Items) : Shape;

internal sealed record ElementShape(Shape Type, IReadOnlyList<IrNullReason> NullableBecause);

/// <summary>An object whose keys are data: what <c>json_objectagg</c> builds.</summary>
internal sealed record RecordShape(Shape Values, IReadOnlyList<IrNullReason> ValuesNullableBecause) : Shape;

/// <summary>One of several types, at least two and all different.</summary>
internal sealed record UnionShape(IReadOnlyList<Shape> Variants) : Shape;

internal sealed record ObjectShape(IReadOnlyList<FieldShape> Fields) : Shape;

/// <summary>
/// A string holding JSON: what a JSON value becomes once a driver returns it as a column, once it passes through a
/// scalar function, or when FOR JSON builds it without its array wrapper.
/// </summary>
/// <param name="Content">What the text parses to when it was built here, which <c>json_query</c> makes live again; null when unknown.</param>
internal sealed record JsonTextShape(Shape? Content = null) : Shape;

/// <summary>
/// The JSON node of a function that calls itself, directly or through others: a reference to a named type that the
/// IR's type table defines once (<see cref="FunctionSummaries.Nodes"/>).
/// </summary>
/// <param name="Function">The function whose node it is.</param>
internal sealed record RecursiveShape(Model.ObjectName Function) : Shape;

/// <summary>
/// No value at all: what a recursive call gives before the fixed point has learnt anything about it. It never reaches
/// the IR; a function whose result is still this when the fixed point is reached never returns.
/// </summary>
/// <param name="At">The function's definition.</param>
internal sealed record NeverShape(SourceLocation At) : Shape;

internal sealed record UnknownShape(string Reason, string Detail, SourceLocation At) : Shape;

internal sealed record FieldShape(string Name, Shape Type, IrPresence Presence, IReadOnlyList<IrNullReason> NullableBecause);

/// <summary>
/// The inferred type of an expression.
/// </summary>
/// <param name="Shape">Its shape when not null.</param>
/// <param name="NullableBecause">Why it can be null; empty when it is proved non-null.</param>
/// <param name="IsLiveJson">
/// Whether it is JSON that <c>FOR JSON</c> and the JSON constructors embed as JSON rather than escaping as a string:
/// the output of a <c>FOR JSON</c> subquery with its array wrapper, of a constructor (<c>json_object</c>, <c>json_array</c>,
/// <c>json_arrayagg</c>, <c>json_objectagg</c>), a value of the <c>json</c> type, <c>json_query</c> over any of them,
/// or a scalar subquery returning one. <see cref="Shape"/> is then the JSON's shape.
/// </param>
/// <param name="Digits">
/// For an <c>int</c> the server may treat as a constant, the integer digits it counts beside a decimal, when not the type's 10
/// (slice 6k, observed: An_int_constant_counts_its_own_digits_beside_a_decimal): a literal its own, carried through a derived
/// table, CTE or view column; a NULL cast to int 1; a CASE, IIF, CHOOSE, COALESCE or ISNULL that the server may fold to one
/// of its literals a range, from the fewest of them to 10. Null for anything else, which counts as its type.
/// </param>
/// <remarks>
/// JSON text whose shape is known (a <c>FOR JSON</c> subquery without its array wrapper, a call of a function returning
/// JSON) has a <see cref="JsonTextShape"/> with its content, which <c>json_query</c> makes live again.
/// </remarks>
/// <param name="JsonWhy">
/// For a string read from a place that holds JSON built here on some writes but may hold other text (<see cref="JsonSlot"/>), why
/// its JSON's shape is not known, naming the write: what <c>json_query</c> over it reports. Null otherwise.
/// </param>
internal sealed record Value(Shape Shape, IReadOnlyList<IrNullReason> NullableBecause, bool IsLiveJson = false, IntDigits? Digits = null, UnknownShape? JsonWhy = null)
{
    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRules.Unknown, shape.At.ToString())]);
}

/// <summary>
/// The integer digits SQL Server may count for an int constant: exactly <paramref name="Least"/> when it equals
/// <paramref name="Most"/>, otherwise any number between, depending on whether it folds the expression to a literal.
/// </summary>
internal sealed record IntDigits(int Least, int Most)
{
    /// <summary>A CASE-like expression the server may fold to one of these values, or leave as an int of 10 digits.</summary>
    public static IntDigits? Foldable(IEnumerable<IntDigits?> results)
    {
        var known = results.Where(d => d is not null).Select(d => d!).ToList();
        return known.Count == 0 ? null : new IntDigits(known.Min(d => d.Least), 10);
    }
}

/// <summary>A result column before it is shaped into a rowset or JSON.</summary>
/// <param name="Origin">
/// The table column it is, unchanged, when it is one (a bare column reference without COLLATE, or a column of
/// <c>SELECT *</c>): what a view's keys are found from.
/// </param>
/// <param name="Expression">
/// The select-list expression it was read from, when there is one: a set operation types an integer literal by its digits
/// and makes a closed set of literals.
/// </param>
internal sealed record RowColumn(string? Name, Value Value, SourceLocation At, BoundColumn? Origin = null, ScalarExpression? Expression = null);
