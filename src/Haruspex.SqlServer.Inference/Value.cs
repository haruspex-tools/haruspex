using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// The inferred type of an expression: an abstract value, which describes every value the expression can have. It is the
/// product of one lattice per fact (<c>Lattice/</c>), joined fact by fact in <see cref="Join"/>; the guide's chapter
/// <c>docs/internals/05-values-as-a-lattice.md</c> explains the idea.
/// </summary>
/// <param name="Shape">Its shape when not null.</param>
/// <param name="NullableBecause">Why it can be null; empty when it is proved non-null (<see cref="NullReasons"/>).</param>
/// <param name="IsLiveJson">
/// Whether it is JSON that <c>FOR JSON</c> and the JSON constructors embed as JSON rather than escaping as a string:
/// the output of a <c>FOR JSON</c> subquery with its array wrapper, of a constructor (<c>json_object</c>, <c>json_array</c>,
/// <c>json_arrayagg</c>, <c>json_objectagg</c>), a value of the <c>json</c> type, <c>json_query</c> over any of them,
/// or a scalar subquery returning one. <see cref="Shape"/> is then the JSON's shape.
/// </param>
/// <param name="JsonWhy">
/// For a string read from a place that holds JSON built here on some writes but may hold other text (<see cref="JsonSlot"/>), why
/// its JSON's shape is not known, naming the write: what <c>json_query</c> over it reports. Null otherwise. It is a witness:
/// <c>json_query</c> over such a string is unknown with or without it, and it only names the reason.
/// </param>
/// <remarks>
/// <para>
/// JSON text whose shape is known (a <c>FOR JSON</c> subquery without its array wrapper, a call of a function returning
/// JSON) has a <see cref="JsonTextShape"/> with its content, which <c>json_query</c> makes live again.
/// </para>
/// <para>
/// <b>Facts dropped by default.</b> <see cref="Digits"/> is a claim that an operation can make false: the digits of
/// <c>1</c> are not those of <c>-1</c> or of <c>@x + 1</c>. So it does not survive an operation unless the operation's rule
/// passes it on. A value made with <c>with</c> has <see cref="IntDigits.Unknown"/> digits, the top, unless the
/// <c>with</c> sets them (the copy constructor below leaves them out, and C# runs it for every <c>with</c>); and a value built
/// new has them too unless its rule states them. A rule that forgets claims less, never something false. <see cref="Pieces"/>
/// are dropped the same way, since an operation that changes a string's text must not keep them. The other facts
/// are kept by <c>with</c>: the shape, the reasons and whether it is live JSON are what <c>with</c> is used to change, and
/// the JSON's why is a witness.
/// </para>
/// </remarks>
internal sealed record Value(Shape Shape, EquatableArray<IrNullReason> NullableBecause, bool IsLiveJson = false, UnknownShape? JsonWhy = null)
{
    /// <summary>The copy <c>with</c> starts from: every fact but <see cref="Digits"/> and <see cref="Pieces"/>, which it drops to the top.</summary>
    private Value(Value original)
    {
        Shape = original.Shape;
        NullableBecause = original.NullableBecause;
        IsLiveJson = original.IsLiveJson;
        JsonWhy = original.JsonWhy;
        Digits = IntDigits.Unknown;
        Pieces = null;
    }

    /// <summary>
    /// For an <c>int</c>, how many integer digits the server counts for it beside a decimal (<see cref="IntDigits"/>):
    /// <see cref="IntDigits.OfType"/> for a column, a variable, a parameter, a function's result or arithmetic; a literal its
    /// own, carried through a derived table, view column or CTE that does not recurse (one that does is never a constant,
    /// observed: A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal); a NULL cast to int 1; a CASE,
    /// IIF, CHOOSE, COALESCE or ISNULL that the server may fold to one of its literals a range, from the fewest of them to 10
    /// (<see cref="IntDigits.Foldable"/>). Unknown (any of 1 to 10) when no rule said. Read only for an <c>int</c>.
    /// </summary>
    public IntDigits Digits { get; init; } = IntDigits.Unknown;

    /// <summary>
    /// For a string, what its text is made of, as far as it may be JSON assembled as text (<see cref="TextPiece"/>): literals,
    /// numbers and escaped strings written as text, JSON values, a <c>string_agg</c> of them. <see cref="JsonAssembly"/> reads
    /// what JSON the pieces make where the text is read as JSON. Null when no rule said, which claims nothing. Dropped by
    /// <c>with</c>, as <see cref="Digits"/> is: an operation that changes the text must not keep its pieces, so only a rule that
    /// states them passes them on.
    /// </summary>
    public EquatableArray<TextPiece>? Pieces { get; init; }

    /// <summary>
    /// The same value with other reasons it may be null: a filter proving it non-null, an outer join or a rollup adding a reason.
    /// Whether it may be null says nothing of what it is, so every other fact is passed on.
    /// </summary>
    public Value WithNullableBecause(EquatableArray<IrNullReason> because) => this with { NullableBecause = because, Digits = Digits, Pieces = Pieces };

    /// <summary>A value of a column, a variable or a computed result: one the server never folds, so <see cref="Digits"/> are its type's.</summary>
    public static Value Computed(Shape shape, EquatableArray<IrNullReason> nullableBecause, bool isLiveJson = false) =>
        new(shape, nullableBecause, isLiveJson) { Digits = IntDigits.OfType };

    public static Value Unknown(UnknownShape shape) =>
        new(shape, [new IrNullReason(IrNullRule.Unknown, shape.At.ToString())]);

    // ---- the knowledge join --------------------------------------------------------------------------------------

    /// <summary>
    /// The knowledge join: a value that is either <paramref name="a"/> or <paramref name="b"/>, null if either may be, of a
    /// shape both fit. What a place holds where paths meet after a branch, where a write adds values to a column of a
    /// temporary table, and where a recursive CTE's members add rows to its anchor's. It is not the server's rule for the type
    /// of a <c>CASE</c> or a set operation, which is <see cref="TypeChain.Of"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It joins each fact in its own lattice: the reasons in <see cref="NullReasons"/>, the digits in <see cref="IntDigits"/>,
    /// the pieces flat (the same pieces stay, any others are none known), and the shape, with whether it is live JSON, in
    /// <see cref="JoinShapes"/>. The JSON's why, a witness, is the left
    /// side's when it has one. An unknown shape allows null, which the reasons then say, and has no why of its own: an
    /// unknown is what <c>json_query</c> over it gives anyway.
    /// </para>
    /// <para>
    /// <b>Laws</b> (held by <c>ValueLatticeTests</c>): idempotent and associative as records, and commutative up to the
    /// order of witnesses (<see cref="Same"/>). Joining values the same up to witnesses gives the left one back as a
    /// record, so an iteration that keeps its state on the left stops when a pass adds nothing.
    /// </para>
    /// </remarks>
    /// <param name="at">Where the two meet, for an unknown.</param>
    /// <param name="what">What holds the two, for an unknown's detail.</param>
    public static Value Join(Value a, Value b, SourceLocation at, string what)
    {
        var because = NullReasons.Join(a.NullableBecause, b.NullableBecause);
        var (shape, live, notJson) = JoinShapes(a, b, at, what);
        var same = a.Shape == b.Shape && a.IsLiveJson == b.IsLiveJson && a.NullableBecause == b.NullableBecause;
        if (shape is UnknownShape unknown)
            return new Value(unknown, same ? because : WithUnknown(because, unknown), live) { Digits = IntDigits.Join(a.Digits, b.Digits) };
        return new Value(shape, because, live, a.JsonWhy ?? b.JsonWhy ?? notJson)
        {
            Digits = IntDigits.Join(a.Digits, b.Digits),
            Pieces = a.Pieces == b.Pieces ? a.Pieces : null,
        };
    }

    /// <summary>
    /// The shape of a value of either side, and whether it is live JSON: one fact, since a shape means one thing as a SQL value
    /// and another as live JSON. The same shape stays; two scalars of one type keep the facets both prove
    /// (<see cref="ScalarFacets"/>); two JSON texts hold either's content (<see cref="JsonShapes"/>); JSON text beside a string
    /// of its own type, <c>nvarchar(max)</c>, is that string, which may not be JSON, so <c>json_query</c> over it reports why.
    /// Anything else, an unknown on either side included, is unknown: a SQL value cannot be a union. The left side's unknown is
    /// kept, a witness.
    /// </summary>
    /// <returns>The shape, whether it is live JSON, and why its JSON is not known when the join is what made it so.</returns>
    private static (Shape Shape, bool IsLiveJson, UnknownShape? NotJson) JoinShapes(Value a, Value b, SourceLocation at, string what)
    {
        if (a.Shape == b.Shape && a.IsLiveJson == b.IsLiveJson)
            return (a.Shape, a.IsLiveJson, null);
        if (a.Shape is UnknownShape known)
            return (known, false, null);
        if (b.Shape is UnknownShape other)
            return (other, false, null);

        if (!a.IsLiveJson && !b.IsLiveJson)
        {
            switch (a.Shape, b.Shape)
            {
                case (JsonTextShape x, JsonTextShape y) when JsonShapes.SameText(x.Text, y.Text):
                    return (new JsonTextShape(x.Content is null || y.Content is null ? null : JsonShapes.Join(x.Content, y.Content)) { Text = JoinedText(x.Text, y.Text) }, false, null);
                case (JsonTextShape json, ScalarShape text) when IsTextOf(json, text):
                    return (JoinedText(json.Text, text) ?? text.WithFacets(ScalarFacets.TypeOnly(text.Type.Facets)), false, NotJson(at, what));
                case (ScalarShape text, JsonTextShape json) when IsTextOf(json, text):
                    return (JoinedText(text, json.Text) ?? text.WithFacets(ScalarFacets.TypeOnly(text.Type.Facets)), false, NotJson(at, what));
            }
        }

        if (a.IsLiveJson == b.IsLiveJson && a.Shape is ScalarShape s && b.Shape is ScalarShape t && s.SameType(t))
            return (s.Joined(t, ScalarFacets.Join(s.Type.Facets, t.Type.Facets)), a.IsLiveJson, null);

        return (new UnknownShape(IrUnknownReason.UnsupportedExpression, $"{what} holds values of different shapes on different paths, which are not joined yet.", at), false, null);
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a string of the type <paramref name="json"/>'s text is: its column's type, or
    /// <c>nvarchar(max)</c> for JSON built here.
    /// </summary>
    private static bool IsTextOf(JsonTextShape json, ScalarShape text) => json.Text is { } own ? own.SameType(text) : SqlTypes.IsNVarCharMax(text);

    /// <summary>Two strings of one type joined, with the facets both prove; null when either is not known as one.</summary>
    private static ScalarShape? JoinedText(ScalarShape? a, ScalarShape? b) =>
        a is not null && b is not null ? a.Joined(b, ScalarFacets.Join(a.Type.Facets, b.Type.Facets)) : null;

    private static UnknownShape NotJson(SourceLocation at, string what) =>
        new(IrUnknownReason.JsonShapeUnknown, $"{what} holds JSON built here on some paths and other text on others, which meet {at}.", at);

    /// <summary>
    /// A value of <paramref name="typed"/>'s SQL type that is either <paramref name="typed"/> or <paramref name="other"/>
    /// converted to that type: what a column holds after a write converts a value to the column's type, and what a
    /// recursive CTE's column holds, whose type is its anchor's exactly (SQL Server refuses any other, error 240, observed).
    /// The conversion, then the knowledge join: a scalar of another type converts to one with only the facets its type
    /// proves, and any other shape is joined as it is. The server never folds a column of a table or of a recursive CTE to a
    /// constant (observed: A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal), so its digits are
    /// the type's.
    /// </summary>
    public static Value JoinConverted(Value typed, Value other, SourceLocation at, string what)
    {
        var converted = typed.Shape is ScalarShape scalar && !typed.IsLiveJson
            && !(other.Shape is ScalarShape o && !other.IsLiveJson && scalar.SameType(o))
            ? new Value(scalar.WithFacets(ScalarFacets.TypeOnly(scalar.Type.Facets)), other.NullableBecause)
            : other;
        return Join(typed, converted, at, what) with { Digits = IntDigits.OfType };
    }

    /// <summary>
    /// Whether two values say the same: equal but for witnesses, which a join keeps from its left side. The reasons are
    /// compared as sets (<see cref="NullReasons.Same"/>), a scalar's facets ignoring their sources and value sets' order
    /// (<see cref="ScalarFacets.Same"/>), JSON content up to its unions' order (<see cref="JsonShapes.Same"/>), and an unknown,
    /// or the JSON's why, as any other. Everything else, the digits, the pieces and whether it is live JSON among them, is
    /// compared exactly. The convergence test of a fixed point is structural equality, which is finer: it can cost a pass, never stop
    /// one early.
    /// </summary>
    public static bool Same(Value a, Value b) =>
        NullReasons.Same(Explained(a), Explained(b))
        && a.Digits == b.Digits
        && a.IsLiveJson == b.IsLiveJson
        && (a.JsonWhy is null) == (b.JsonWhy is null)
        && a.Pieces == b.Pieces
        && JsonShapes.Same(a.Shape, b.Shape);

    /// <summary>
    /// Whether every value <paramref name="a"/> allows, <paramref name="b"/> allows: the product of the facts' orders. Anything
    /// is below a value of unknown shape (but for its reasons, which still say why it may be null); otherwise the same
    /// liveness, the reasons a subset (<see cref="NullReasons.Below"/>), the digits within the other's (<see cref="IntDigits.Below"/>),
    /// the same pieces or the other's none known,
    /// and the shape below the other's: JSON text below a string of <c>nvarchar(max)</c> that proves only what its type does,
    /// and anything else by <see cref="JsonShapes.Below"/>. Written apart from <see cref="Join"/>, so that the law tests can
    /// check the join against it.
    /// </summary>
    public static bool Below(Value a, Value b)
    {
        if (!NullReasons.Below(Explained(a), Explained(b)) || !IntDigits.Below(a.Digits, b.Digits) || (b.Pieces is not null && a.Pieces != b.Pieces))
            return false;
        if (b.Shape is UnknownShape)
            return true;
        if (a.IsLiveJson != b.IsLiveJson)
            return false;
        return (a.Shape, b.Shape) switch
        {
            (JsonTextShape, ScalarShape { Type: var text } shape) when SqlTypes.IsNVarCharMax(shape) => !a.IsLiveJson && ScalarFacets.Same(text.Facets, ScalarFacets.TypeOnly(text.Facets)),
            _ => JsonShapes.Below(a.Shape, b.Shape),
        };
    }

    /// <summary>
    /// The reasons a value may be null that say something of it: of a value of unknown shape, those that do not merely name
    /// the unknown, which is a witness (any unknown allows null).
    /// </summary>
    private static IEnumerable<IrNullReason> Explained(Value value) =>
        value.Shape is UnknownShape ? value.NullableBecause.Where(r => r.Rule != IrNullRule.Unknown) : value.NullableBecause;

    /// <summary>A value whose shape is unknown proves nothing, so it may be null, which its reasons say.</summary>
    private static EquatableArray<IrNullReason> WithUnknown(EquatableArray<IrNullReason> because, UnknownShape unknown) =>
        NullReasons.Join(because, [new IrNullReason(IrNullRule.Unknown, unknown.At.ToString())]);

}
