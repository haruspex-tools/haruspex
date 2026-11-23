using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// The common expressions. Types come from ScalarType.cs; README.md in this folder ("Expressions") gives the rules
// and the observations they rest on (ExpressionObservationTests in the differential tests).
internal sealed partial class Typer
{
    // ---- literals -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A string literal: varchar or nvarchar of its length, at least 1, never null (observed:
    /// A_literals_type_is_its_own_digits_or_characters). A varchar literal's length counts bytes of the database's code page,
    /// which the project does not show, so one with a character beyond ASCII is not typed; nor one with COLLATE, which may
    /// change the code page.
    /// </summary>
    private Value FromString(StringLiteral literal) =>
        LiteralType(literal) is { } type
            ? new Value(SqlTypes.ToConvertedShape(type, At(literal)), []) { Pieces = [new LiteralPiece(literal.Value, literal.IsNational, At(literal))] }
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The literal {Text(literal)} is not ASCII, so its length in bytes depends on the code page.", At(literal)));

    /// <summary>
    /// A string literal holding a SQLCMD variable, <c>'$(Region)'</c>: unknown, since a deployment puts the variable's value in its
    /// place as text before the server parses it. The value may be empty or of any length, and one with a quotation mark ends the
    /// string, so not even the type is the written text's (decision 0046; SqlcmdVariableObservationTests). A script's own
    /// <c>:setvar</c> has been applied by then, so what is left is a variable whose value only the deployment knows.
    /// </summary>
    private Value Replaced(StringLiteral literal, string variable) =>
        Value.Unknown(Unsupported(
            IrUnknownReason.SqlcmdVariable,
            $"The literal {Text(literal)} holds the SQLCMD variable {variable}, which a deployment replaces with its value, so its length, value and type are not the text's.",
            At(literal)));

    private static ScalarType? LiteralType(StringLiteral literal) =>
        literal.Collation is not null || (!literal.IsNational && literal.Value.Any(c => c > '\u007f'))
            ? null
            : new ScalarType(literal.IsNational ? BuiltInType.NVarChar : BuiltInType.VarChar, Math.Max(1, literal.Value.Length));

    /// <summary>
    /// A decimal literal, <c>2.50</c>: numeric whose scale is its digits after the point, trailing zeros too, and whose
    /// precision adds the digits before it, less leading zeros (observed: A_literals_type_is_its_own_digits_or_characters).
    /// </summary>
    private Value FromNumeric(NumericLiteral literal)
    {
        var text = literal.Value;
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var whole = (point < 0 ? text : text[..point]).TrimStart('0');
        var scale = point < 0 ? 0 : text.Length - point - 1;
        var precision = Math.Max(1, whole.Length + scale);
        if (precision > 38)
            return Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The literal {literal.Value} has more than 38 digits.", At(literal)));
        return new Value(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.Numeric, Precision: precision, Scale: scale), At(literal)), []);
    }

    /// <summary>
    /// A binary literal, <c>0x0A1B</c>: varbinary of its bytes, two hex digits each, an odd digit padded to a byte with a
    /// leading zero; <c>0x</c>, which holds none, is varbinary(1), and past 8000 bytes it is varbinary(max) (observed:
    /// A_binary_literal_is_varbinary_of_its_bytes). Never null.
    /// </summary>
    private Value FromBinary(BinaryLiteral literal)
    {
        var bytes = (literal.Value.Length - "0x".Length + 1) / 2;
        var length = bytes > 8000 ? -1 : Math.Max(1, bytes);
        return new Value(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.VarBinary, length), At(literal)), []);
    }

    /// <summary>The scalar type of a value, when it is a built-in scalar.</summary>
    private static ScalarType? TypeOf(Value value)
    {
        if (value.IsLiveJson)
            return null;
        return value.Shape switch
        {
            ScalarShape { BuiltIn: var type } => type,

            // A column's JSON is a string of the column's type; JSON built here is nvarchar(max).
            JsonTextShape { Text: { } text } => text.BuiltIn,
            JsonTextShape => new ScalarType(BuiltInType.NVarChar, -1),
            _ => null,
        };
    }

    /// <summary>
    /// A value as an operand of a type rule: its type, and the integer digits the server counts for it. An integer literal
    /// counts its own, and so does an int the server treats as a constant (<see cref="Value.Digits"/>): its fewest when
    /// <paramref name="least"/>, else its most. Anything else its type's.
    /// </summary>
    /// <param name="value">The operand's value.</param>
    /// <param name="expression">The expression it came from.</param>
    /// <param name="least">Whether to count the fewest digits the value may have, rather than the most.</param>
    /// <param name="query">Whether it is a set operation's query as it stands, rather than inside an expression.</param>
    /// <param name="arithmetic">Whether it is an operand of arithmetic, where NULLIF of two literals is folded.</param>
    private static Operand? OperandOf(Value value, ScalarExpression expression, bool least = false, bool query = false, bool arithmetic = false)
    {
        if (TypeOf(value) is not { } type)
            return null;
        if (IntegerLiteralOf(expression) is var (literal, negative))
            return new Operand(type, IntDigits.CountOf(literal.Value, negative));

        // Inside an expression the server counts a column's type's digits even when it is a derived table's literal; only as a
        // set operation's query, as it stands, does a column count its literal's digits. NULLIF of two literals it folds in
        // arithmetic only; beside a COALESCE's, a CASE's or a query's other values it counts its type's (observed:
        // Nullif_of_two_literals_is_folded_inside_arithmetic_but_not_beside_other_values,
        // A_derived_tables_literal_counts_its_own_digits_only_as_a_set_operations_query).
        if (arithmetic && FoldedNullIfDigits(expression) is { } folded)
            return new Operand(type, folded);
        if (!query && Strip(expression) is ColumnReferenceExpression)
            return Operand.OfType(type);
        if (type.Type == BuiltInType.Int && (least ? value.Digits.Least : value.Digits.Most) is var d and < 10)
            return new Operand(type, d);
        return Operand.OfType(type);
    }

    /// <summary>Whether the server may count an int's digits as a literal's or as its type's, depending on what it folds.</summary>
    private static bool Uncertain(Value value) => value.Shape is ScalarShape { BuiltIn.Type: var type } && type == BuiltInType.Int && !value.IsLiveJson && value.Digits.IsUncertain;

    /// <summary>
    /// An integer literal, through parentheses and signs, with whether a minus sign is the sign before it. Only one sign is ever
    /// counted: a literal signed more than once is unknown wherever it is typed (<see cref="Unary"/>), before its digits are
    /// asked for.
    /// </summary>
    private static (IntegerLiteral Literal, bool Negative)? IntegerLiteralOf(ScalarExpression expression) => Strip(expression) switch
    {
        IntegerLiteral literal => (literal, false),
        UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary =>
            IntegerLiteralOf(unary.Expression) is var (literal, _) ? (literal, unary.UnaryExpressionType == UnaryExpressionType.Negative) : null,
        _ => null,
    };

    /// <summary>
    /// A literal, through parentheses, with whether it is negated; null for anything else, and for a literal holding a SQLCMD
    /// variable, whose text is not its value (<see cref="Replaced"/>), or under <c>COLLATE</c>. Every rule that reads a literal's text (a value set, a
    /// JSON document, a conversion that fits) finds it here, so none of them reads the written text of one.
    /// </summary>
    internal static (Literal Literal, bool Negative)? LiteralOf(ScalarExpression expression)
    {
        var negative = false;
        var e = Strip(expression);
        while (e is UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary)
        {
            negative ^= unary.UnaryExpressionType == UnaryExpressionType.Negative;
            e = Strip(unary.Expression);
        }

        // A literal under COLLATE may not hold the characters written (Collated), so it is not read as one. As for Strip, no
        // wrong claim reaches this today (a literal that isn't ASCII and isn't N'' is unknown already); it costs only the set
        // of an N'' literal under COLLATE.
        return e is Literal { Collation: null } literal && !SqlcmdVariableReference.IsIn(literal) ? (literal, negative) : null;
    }

    /// <summary>A value the server computes, of this type: never folded to a constant, so it counts its type's digits.</summary>
    private Value Typed(ScalarType type, EquatableArray<IrNullReason> because, TSqlFragment at) =>
        Value.Computed(SqlTypes.ToConvertedShape(type, At(at)), because);

    /// <summary>
    /// The reason an overflow or a division by zero at <paramref name="at"/> gives NULL: none when the callers' sessions make
    /// it an error (<see cref="SessionOptions.OverflowIsAnError"/>), which fails the call and so describes nothing the IR does.
    /// </summary>
    private EquatableArray<IrNullReason> Overflow(TSqlFragment at) => Overflow(summaries.Session, At(at));

    /// <inheritdoc cref="Overflow(TSqlFragment)"/>
    private static EquatableArray<IrNullReason> Overflow(SessionOptions session, SourceLocation at) =>
        session.OverflowIsAnError ? [] : [new IrNullReason(IrNullRule.ArithmeticOverflow, at.ToString())];

    /// <summary>
    /// The reason a decimal rounded to fewer places at <paramref name="at"/> gives NULL: none when the callers' sessions make the
    /// rounding silent or an error (<see cref="SessionOptions.RoundingMayBeNull"/>). Under TRY_CAST and TRY_CONVERT, an
    /// <paramref name="attempt"/>, only NUMERIC_ROUNDABORT declared off removes it.
    /// </summary>
    private EquatableArray<IrNullReason> PrecisionLoss(TSqlFragment at, bool attempt = false) => PrecisionLoss(summaries.Session, At(at), attempt);

    /// <inheritdoc cref="PrecisionLoss(TSqlFragment, bool)"/>
    private static EquatableArray<IrNullReason> PrecisionLoss(SessionOptions session, SourceLocation at, bool attempt = false) =>
        session.RoundingMayBeNull(attempt) ? [new IrNullReason(IrNullRule.PrecisionLoss, at.ToString())] : [];

    /// <summary>
    /// Whether converting <paramref name="value"/>, read from <paramref name="expression"/> when there is one, to
    /// <paramref name="to"/> may round it to fewer places: a string literal by the places it is written with, anything else by
    /// its type (<see cref="Conversions.Rounds(ScalarType, ScalarType)"/>). A value whose type is not known may, into a decimal.
    /// </summary>
    private static bool Rounds(Value value, ScalarExpression? expression, ScalarType to) =>
        (expression is not null && LiteralOf(expression) is ({ } literal, _) ? Conversions.Rounds(literal, to) : null)
        ?? (TypeOf(value) is { } from ? Conversions.Rounds(from, to) : to.IsDecimal);

    /// <summary>
    /// Why converting <paramref name="value"/>, read from <paramref name="expression"/> when there is one, to
    /// <paramref name="to"/> at <paramref name="at"/> may give NULL besides the value's own reasons: an overflow unless it
    /// <paramref name="fits"/> (under TRY_CAST and TRY_CONVERT, an <paramref name="attempt"/>, a failed conversion instead), and a
    /// rounding to fewer places when it may round (<see cref="Rounds"/>), each as the callers' session makes it NULL. Every
    /// conversion inference reads gives its reasons here: CAST and CONVERT, an assignment or argument, the common type of a
    /// chain, ISNULL's second argument, LAG's default.
    /// </summary>
    private static EquatableArray<IrNullReason> ConversionReasons(
        SessionOptions session, Value value, ScalarExpression? expression, ScalarType to, bool fits, SourceLocation at, bool attempt = false) =>
    [
        .. fits ? [] : attempt ? [new IrNullReason(IrNullRule.FailedConversion, at.ToString())] : Overflow(session, at),
        .. Rounds(value, expression, to) ? PrecisionLoss(session, at, attempt) : [],
    ];

    private Value NotModelled(TSqlFragment expression, string why) =>
        Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{Text(expression)}: {why}", At(expression)));

    // ---- combining types --------------------------------------------------------------------------------------------

    /// <summary>
    /// The type values of different types take when they meet in one column: a set operation's, COALESCE's, a CASE's, IIF's or
    /// CHOOSE's, by the server's type rule for a chain (<see cref="TypeChain.Of"/>), a fold from left to right. It is not the
    /// knowledge join (<see cref="Value.Join"/>), which says what is known of a value that may come from either side.
    /// </summary>
    /// <param name="Type">The type, or null when it is not known.</param>
    /// <param name="Converted">For each value, the reasons converting it may give NULL: an overflow, NULL under a caller's
    /// ANSI_WARNINGS and ARITHABORT off (observed: A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options),
    /// unless it cannot (<see cref="Conversions.NeverNull"/>, or a literal that fits); and a rounding to the type's fewer places,
    /// NULL under NUMERIC_ROUNDABORT on as well (<see cref="Rounds"/>; observed for COALESCE, CASE, IIF, CHOOSE and UNION ALL).
    /// Empty when neither can happen.</param>
    /// <param name="Why">When the type is not known, why, naming the types so a user knows what to change.</param>
    private sealed record ChainType(ScalarType? Type, EquatableArray<EquatableArray<IrNullReason>> Converted, string? Why);

    /// <param name="values">Each value, the expression it came from (for a literal's digits and value), and where an overflow
    /// converting it would happen.</param>
    /// <param name="queries">Whether the values are a set operation's queries, each as it stands, rather than inside an
    /// expression (<see cref="OperandOf"/>).</param>
    private ChainType Chained(IReadOnlyList<(Value Value, ScalarExpression Expression, SourceLocation At)> values, bool queries = false)
    {
        var operands = values.Select(v => OperandOf(v.Value, v.Expression, query: queries)).ToList();
        var types = operands.Select(o => o?.Type).ToList();
        static string Name(Value value, ScalarType? type) => type?.Text ?? value.Shape switch
        {
            ScalarShape { Type.Sql: { } sql } => sql,
            _ when value.IsLiveJson => "JSON",
            _ => "a value of another kind",
        };

        static string Names(IEnumerable<string> names)
        {
            var distinct = names.Distinct(StringComparer.Ordinal).ToList();
            return distinct.Count == 1 ? distinct[0] : string.Join(", ", distinct.SkipLast(1)) + " and " + distinct[^1];
        }

        if (types.Any(t => t is null))
            return new ChainType(null, [], $"combining {Names(values.Select((v, i) => Name(v.Value, types[i])))} is not modelled yet.");
        var known = operands.OfType<Operand>().ToList();
        if (TypeChain.Clash(known) is var (first, second))
            return new ChainType(null, [], $"SQL Server refuses to combine {first.Text} and {second.Text}: there is no implicit conversion between them (206, an operand type clash).");
        if (TypeChain.Of(known) is not { } type)
            return new ChainType(null, [], $"combining {Names(known.Select(o => o.Type.Text))} is not modelled yet.");

        // An int the server may or may not fold to a literal counts between that literal's digits and 10; when that changes the
        // scale, which values come back is not known (observed: An_int_constant_counts_its_own_digits_beside_a_decimal). When it
        // changes only the precision, the most digits hold every value.
        // Never null: each value's operand was found above, and one by its fewest digits is found with it (OperandOf).
        if (values.Any(v => Uncertain(v.Value)) && TypeChain.Of([.. values.Select(v => OperandOf(v.Value, v.Expression, least: true, query: queries)!)]) is var fewest
            && (fewest is null || fewest.Type != type.Type || fewest.Scale != type.Scale))
        {
            return new ChainType(null, [], $"its type depends on whether SQL Server folds {Names(values.Where(v => Uncertain(v.Value)).Select(v => Text(v.Expression)))} to a constant, whose digits it would count.");
        }

        var converted = values.Select((v, i) =>
        {
            var fits = (LiteralOf(v.Expression) is ({ } literal, var negative) ? Conversions.Fits(literal, type, negative) : null)
                ?? Conversions.NeverNull(known[i], type);
            return ConversionReasons(summaries.Session, v.Value, v.Expression, type, fits, v.At);
        }).ToList();
        return new ChainType(type, [.. converted], null);
    }

    // ---- COALESCE and ISNULL ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>COALESCE(a, b, …)</c>: the type of highest precedence (<see cref="TypeChain.Of"/>, <see cref="Chained"/>), to which every argument
    /// converts without an overflow. It is <c>CASE WHEN a IS NOT NULL THEN a …</c>, so it is never null when an argument is
    /// never null, except that an argument before it that is a subquery or a call is read twice and may be NULL the second
    /// time (observed: Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot), which is <c>read-twice</c>: a value
    /// tested NULL is passed over, so only the second read can make it NULL. No seed reaches it, so it has a rule of its own.
    /// A NULL literal adds nothing but its place.
    /// </summary>
    private Value Coalesce(CoalesceExpression coalesce, ReadScope scope)
    {
        var arguments = coalesce.Expressions.Where(e => Strip(e) is not NullLiteral).ToList();
        if (arguments.Count == 0)
            return NotModelled(coalesce, "COALESCE of NULL only is an error.");
        var values = arguments.Select(a => Expression(a, scope)).ToList();
        if (values.FirstOrDefault(v => v.Shape is UnknownShape) is { } unknown)
            return unknown;
        var first = values.FindIndex(v => v.NullableBecause.Count == 0);
        var because = first < 0
            ? values.Select(v => v.NullableBecause).Aggregate(NullReasons.Join)
            : values.Take(first).Select((v, i) => i).Where(i => ReadTwice(arguments[i]))
                .Select(i => (EquatableArray<IrNullReason>)[new IrNullReason(IrNullRule.ReadTwice, At(arguments[i]).ToString())])
                .Aggregate((EquatableArray<IrNullReason>)[], NullReasons.Join);
        if (values.Any(IsJson) || ReferenceEquals(coalesce, scope.FoldingMoot))
        {
            var json = CoalesceJson(coalesce, arguments, values, because, scope);
            if (values.Any(IsJson) || json.Shape is JsonTextShape)
                return json;
        }

        // A value converted to the common type may overflow it, or be rounded to its fewer places, NULL under the caller's
        // options, whichever argument it is (observed: A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options,
        // and for COALESCE in PrecisionLossObservationTests).
        var at = At(coalesce);
        var combined = Chained([.. values.Select((v, i) => (v, arguments[i], at))]);
        if (combined.Type is not { } type)
            return NotModelled(coalesce, combined.Why!); // Never null: a chain without a type says why (Chained).

        // COALESCE(NULL, 1) is folded to 1 and counts its digits (observed: An_int_constant_counts_its_own_digits_beside_a_decimal);
        // one whose first argument is never constant is never folded (Only_expressions_over_literals_are_folded_when_a_type_is_derived).
        return Typed(type, combined.Converted.Aggregate(because, NullReasons.Join), coalesce) with
        {
            Digits = type.Type == BuiltInType.Int && !NeverConstant(arguments[0]) ? IntDigits.Foldable(values.Select(v => v.Digits)) : IntDigits.OfType,
            Pieces = EitherPieces(arguments, values, type),
        };
    }

    /// <summary>
    /// Whether an expression may give another value when it is read again in one row: one with a subquery, or a call other
    /// than an aggregate (computed once per group) or one of the built-ins modelled here, which read only their arguments.
    /// </summary>
    private static bool ReadTwice(ScalarExpression expression)
    {
        var finder = new VolatileFinder();
        expression.Accept(finder);
        return finder.Found;
    }

    private sealed class VolatileFinder : TSqlFragmentVisitor
    {
        // A built-in function the table says reads only its arguments: the date, string and numeric functions, ISNULL, CONCAT
        // and CHOOSE; the current date and time do not, but are never NULL, so a COALESCE never reads them twice. LEFT and
        // RIGHT are not FunctionCalls, so never counted.
        public bool Found { get; private set; }

        public override void Visit(ScalarSubquery node) => Found = true;

        public override void Visit(FunctionCall node) =>
            Found |= !(node.CallTarget is null && node.OverClause is null && (IsAggregate(node) || TypingOf(node) is { ReadsOnlyItsArguments: true }));
    }

    /// <summary>
    /// <c>ISNULL(a, b)</c>: a's type, to which b is converted, a longer string cut short (observed:
    /// Isnull_converts_its_second_argument_to_the_firsts_type_cutting_a_string_short); b's type when a is the NULL literal. It
    /// reads a once, so it is never null when a is never null; nor when b is never null and converts to a's type without an
    /// overflow or a rounding to fewer places, either of which would make it NULL under a caller's session options (observed
    /// for ISNULL(@ti, 1000), and in PrecisionLossObservationTests). A value never
    /// null is a itself, with its facts; otherwise only a's type's facts hold, since b may be outside a's CHECK set.
    /// </summary>
    private Value IsNull(FunctionCall call, ReadScope scope)
    {
        var (a, b) = (call.Parameters[0], call.Parameters[1]);
        if (Strip(a) is NullLiteral)
            return Strip(b) is NullLiteral ? NotModelled(call, "ISNULL of NULL and NULL is not typed.") : Expression(b, scope) with { Digits = IntDigits.OfType };
        var first = Expression(a, scope);
        if (first.Shape is UnknownShape)
            return first;

        // b is converted to a's type implicitly, which the server refuses for some pairs (257, 206) whether or not a can be NULL
        // (observed in CatalogueSweepGridTests; the chart is ConversionChart).
        var second = Strip(b) is NullLiteral || IsJson(first) ? null : Expression(b, scope);
        if (TypeOf(first) is { } firstType && second is not null && TypeOf(second) is { } secondType
            && ConversionChart.Of(secondType.Type, firstType.Type) != ConversionKind.Implicit)
        {
            return NotModelled(call, $"SQL Server refuses it: {secondType.Text} doesn't convert implicitly to {firstType.Text}.");
        }

        // ISNULL is never folded, so an int counts its type's digits even when a is a literal (observed:
        // Only_expressions_over_literals_are_folded_when_a_type_is_derived).
        if (first.NullableBecause.Count == 0)
            return first with { Digits = IntDigits.OfType, Pieces = first.Pieces };
        if (IsJson(first))
            return Strip(b) is NullLiteral ? first : IsNullJson(call, a, first, b, scope);
        if (TypeOf(first) is not { } type)
            return NotModelled(call, "ISNULL of a value of this type is not modelled yet.");
        if (second is null)
            return Typed(type, first.NullableBecause, call);
        if (second.Shape is UnknownShape)
            return second;
        var fits = (LiteralOf(b) is ({ } literal, var negative) ? Conversions.Fits(literal, type, negative) : null)
            ?? (OperandOf(second, b) is { } from && Conversions.Converts(from, type));
        // An overflow converting the second, or a rounding to a's fewer places, is NULL, or an error that fails the call when
        // the callers' sessions make it one (or silent, for the rounding); then ISNULL of a second never null is never null.
        var converting = ConversionReasons(summaries.Session, second, b, type, fits, At(call));
        var either = second.NullableBecause.Count == 0 && converting.Count == 0
            ? Typed(type, [], call)
            : Typed(type, NullReasons.Join(NullReasons.Join(first.NullableBecause, second.NullableBecause), converting), call);
        return either with { Digits = either.Digits, Pieces = EitherPieces([a, b], [first, second], type) };
    }

    // ---- CAST and CONVERT -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CAST(x AS t)</c>, <c>CONVERT(t, x [, style])</c>, and their <c>TRY_</c> forms: of type t whatever the style (observed:
    /// Cast_and_convert_are_of_the_target_type_whatever_the_style), null when x is; and, unless every value of x's type
    /// converts to t without an overflow (<see cref="Conversions.Converts"/>), NULL under a caller's ANSI_WARNINGS and ARITHABORT
    /// off (<c>arithmetic-overflow</c>), or for TRY_ whenever the conversion may fail (<c>failed-conversion</c>, observed:
    /// Try_cast_and_try_convert_are_null_when_the_conversion_fails). A decimal rounded to fewer places is NULL under
    /// NUMERIC_ROUNDABORT on (<c>precision-loss</c>, <see cref="Rounds"/>), with ANSI_WARNINGS and ARITHABORT off, or for TRY_
    /// whatever they are (observed: Under_try_cast_a_loss_of_precision_is_null_whenever_numeric_roundabort_is_on). A string
    /// type without a length is 30 long here, where a declaration's is 1 (observed:
    /// A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration), and so is a binary one (observed:
    /// A_binary_type_without_a_length_is_30_bytes_long_in_a_conversion). A conversion to x's own type changes
    /// nothing, so the value keeps its facts. The format a style gives is not claimed, but a style that is NULL makes the
    /// result NULL, whatever is converted, even an int into an int, so the style's reasons are the result's and the NULL
    /// literal's is <c>null-literal</c> (observed: A_null_style_makes_convert_and_try_convert_null_whatever_they_convert). JSON
    /// built here converts by rules of its own (<see cref="JsonCast"/>, <see cref="FromLiveJson"/>).
    /// </summary>
    private Value Cast(DataTypeReference target, ScalarExpression operand, TSqlFragment call, ReadScope scope, ScalarExpression? style = null, bool attempt = false)
    {
        var converted = Conversion(target, operand, call, scope, styled: style is not null, attempt);
        if (style is null || converted.Shape is UnknownShape)
            return converted;
        var styling = Expression(style, scope);
        return styling.Shape is UnknownShape ? styling : converted.WithNullableBecause(NullReasons.Join(converted.NullableBecause, styling.NullableBecause));
    }

    /// <summary>The conversion <see cref="Cast"/> describes, before a style's reasons are joined to it.</summary>
    private Value Conversion(DataTypeReference target, ScalarExpression operand, TSqlFragment call, ReadScope scope, bool styled, bool attempt)
    {
        var type = SyntaxReader.Type(target);
        var resolved = SchemaRules.ResolveType(model, type)?.Scalar;
        if (resolved?.Type == BuiltInType.Json && Strip(operand) is not NullLiteral)
            return JsonCast(operand, call, scope);
        var to = SqlTypes.Modelled(resolved);
        if (to is { IsString: true } or { IsBinary: true } && type is { Kind: SqlTypeKind.BuiltIn, Arguments.Count: 0 })
            to = to with { Length = BuiltInType.LengthInConversion };
        var shape = to is null ? null : SqlTypes.ToConvertedShape(to, At(call));
        if (to is null || shape is null or UnknownShape)
            return NotModelled(call, "a conversion to this type is not modelled yet.");

        // The NULL literal converts to any type, and is NULL there (CAST(NULL AS date) is a common way to type a NULL).
        // As an int it counts 1 integer digit beside a decimal, as a literal would (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal: CAST(NULL AS int) beside decimal(38,38) is decimal(38,37)).
        if (Strip(operand) is NullLiteral)
            return new Value(shape, [new IrNullReason(IrNullRule.NullLiteral, At(operand).ToString())]) { Digits = to.Type == BuiltInType.Int ? IntDigits.Literal(1) : IntDigits.OfType };

        var value = Expression(operand, scope);
        if (value.Shape is UnknownShape)
            return value;
        if (value.IsLiveJson)
            return FromLiveJson(value, operand, to, shape, call, styled);

        var from = OperandOf(value, operand);

        // A conversion the chart refuses is an error when the procedure is created (529), so it describes nothing that runs
        // (observed in CatalogueSweepGridTests; the chart is ConversionChart).
        if (from is not null && ConversionChart.Of(from.Type.Type, to.Type) == ConversionKind.Refused)
            return NotModelled(call, $"SQL Server refuses to convert {from.Type.Text} to {to.Text}.");
        // Into a binary type a style says only how text is read as hex, and text that isn't is an error (8114), never NULL, but under
        // TRY_CONVERT (observed: Text_read_as_hex_into_binary_is_an_error_when_it_is_not_hex_and_null_under_try_convert).
        var fits = (!styled && LiteralOf(operand) is ({ } literal, var negative) ? Conversions.Fits(literal, to, negative) : null)
            ?? (from is not null && Conversions.Converts(from, to, styled && (attempt || !to.IsBinary)));
        // A NULL cast to int and cast again stays that constant; CAST(1 AS int) counts an int's 10 digits (same observation).
        if (fits && from?.Type.Text == to.Text && !styled)
            return value.Digits == IntDigits.Literal(1) && IsNullCast(operand) ? value : value with { Digits = IntDigits.OfType, Pieces = value.Pieces };
        return Value.Computed(shape, NullReasons.Join(value.NullableBecause, ConversionReasons(summaries.Session, value, operand, to, fits, At(call), attempt)))
            with
        { Digits = IntDigits.OfType, Pieces = ConvertedPieces(value, to, shape, call) };
    }

    /// <summary>The NULL literal, cast or converted, however many times.</summary>
    private static bool IsNullCast(ScalarExpression expression) => Strip(expression) switch
    {
        NullLiteral => true,
        CastCall cast => IsNullCast(cast.Parameter),
        ConvertCall convert => IsNullCast(convert.Parameter),
        TryCastCall cast => IsNullCast(cast.Parameter),
        TryConvertCall convert => IsNullCast(convert.Parameter),
        _ => false,
    };

    // ---- strings --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What a value adds to a string built by CONCAT: a string its length (bytes for varchar, characters for nvarchar), an int
    /// 12 and a bit 1 (observed), the NULL literal (no value) nothing; a binary value its bytes, read as varchar (observed:
    /// Concat_reads_a_binary_value_as_varchar_of_its_bytes); null for anything else, whose length is not modelled.
    /// </summary>
    private static (ScalarType Type, int Length)? Piece(Value? value)
    {
        if (value is null)
            return (new ScalarType(BuiltInType.VarChar, 0), 0);
        return TypeOf(value) switch
        {
            { IsString: true } s => (s, s.Length),
            { IsBinary: true } b => (new ScalarType(BuiltInType.VarChar, b.Length), b.Length),
            { } i when i.Type == BuiltInType.Int => (i, 12),
            { IsBit: true } b => (b, 1),
            _ => null,
        };
    }

    /// <summary>
    /// <c>CONCAT(a, b, …)</c> and <c>CONCAT_WS(sep, a, b, …)</c>: never null, whatever is (a NULL counts as empty); nvarchar if
    /// any argument is, the separator included; as long as the arguments together, plus the separator between each two; max if
    /// any is, otherwise cut at 8000 bytes, 4000 characters for nvarchar (observed:
    /// Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null).
    /// </summary>
    private Value Concat(FunctionCall call, ReadScope scope, bool separated)
    {
        var arguments = call.Parameters.ToList();
        if (arguments.Count < (separated ? 3 : 2))
            return NotModelled(call, "too few arguments.");
        // An argument already unknown makes the whole unknown for its own reason, so the note says why (a column not found, a
        // SQLCMD variable) rather than that a length isn't modelled. Live JSON whose shape is unknown, a json column's, is a value
        // of known kind, not an unknown one, and SQL Server refuses CONCAT of json (Msg 257), so it stays not modelled.
        var values = arguments.Select(a => Strip(a) is NullLiteral ? null : Expression(a, scope)).ToList();
        if (values.FirstOrDefault(v => v is { IsLiveJson: false, Shape: UnknownShape }) is { } unknown)
            return unknown;
        var pieces = values.Select((v, i) => v is not null && LiveText(v, arguments[i]) is { } live ? (live, -1) : Piece(v)).ToList();
        if (pieces.Any(p => p is null))
            return NotModelled(call, "an argument whose length as a string is not modelled yet.");

        // Beside nvarchar the server reads a binary value's bytes as UTF-16, two to a character (observed in the same test).
        if (values.Any(v => v is not null && TypeOf(v) is { IsBinary: true }) && pieces.Any(p => p is { Type.IsNational: true }))
            return NotModelled(call, "a binary value beside nvarchar, whose bytes it reads as characters, is not modelled yet.");

        var known = pieces.OfType<(ScalarType Type, int Length)>().ToList();
        var parts = separated ? [.. known.Skip(1)] : known;
        var separator = separated ? known[0].Length * (parts.Count - 1) : 0;
        var max = known.Any(p => p.Type.IsMax);
        var national = known.Any(p => p.Type.IsNational);
        var length = parts.Sum(p => p.Length) + separator;
        var type = ScalarType.CharacterString(national, varying: true, max ? -1 : length);
        var concatenated = Typed(type, [], call);
        return concatenated with { Digits = concatenated.Digits, Pieces = ConcatPieces(arguments, values, separated, max, !max && length > type.Type.MostLength) };
    }

    /// <summary>
    /// String <c>a + b</c>: as CONCAT for its type, but fixed-length when both operands are (char, or nchar if either is:
    /// observed, Fixed_length_strings_plus_is_fixed_length), and null when either operand is. With
    /// CONCAT_NULL_YIELDS_NULL off, which only a session can set and is deprecated, NULL + 'x' is 'x'
    /// (With_concat_null_yields_null_off_a_string_plus_null_is_the_string), so only when both are: when the callers' sessions
    /// are declared to set it off, an operand never null makes the sum never null. Not known, the claim of either is null holds
    /// under both settings, since off is only less often null.
    /// </summary>
    /// <remarks>
    /// Two binary values join as two strings of one byte a character do: binary when both are, else varbinary, as long as both up to
    /// 8000 bytes, max if either is, and NULL by the same rule (observed:
    /// Binary_plus_is_as_long_as_both_up_to_8000_bytes_and_fixed_only_when_both_are).
    /// </remarks>
    private Value Plus(BinaryExpression plus, Value left, Value right, ScalarType l, ScalarType r)
    {
        var type = l.IsBinary
            ? ScalarType.ByteString(varying: l.IsVarying || r.IsVarying, l.IsMax || r.IsMax ? -1 : l.Length + r.Length)
            : ScalarType.CharacterString(l.IsNational || r.IsNational, varying: l.IsVarying || r.IsVarying, l.IsMax || r.IsMax ? -1 : l.Length + r.Length);
        var sum = Typed(
            type,
            summaries.Session.ConcatenatingNullKeepsTheString && (left.NullableBecause.Count == 0 || right.NullableBecause.Count == 0)
                ? []
                : NullReasons.Join(left.NullableBecause, right.NullableBecause),
            plus);
        if (l.IsBinary)
            return sum;

        // JSON assembled as text (Typer.JsonAssembled.cs): a NULL operand makes the sum NULL, unless the callers' sessions may
        // set CONCAT_NULL_YIELDS_NULL off, under which it counts as empty. An operand that is itself a sum already has its own
        // operands' empty alternatives: off, it is NULL only when they all are, and on, its NULL makes this sum NULL too.
        var nullIsEmpty = summaries.Session.ConcatNullYieldsNull != true;
        return sum with
        {
            Digits = sum.Digits,
            Pieces = Joined(
                [
                    (OrEmpty(PiecesOf(left, plus.FirstExpression), left, nullIsEmpty && !IsSum(plus.FirstExpression)), plus.FirstExpression),
                    (OrEmpty(PiecesOf(right, plus.SecondExpression), right, nullIsEmpty && !IsSum(plus.SecondExpression)), plus.SecondExpression),
                ],
                type.IsMax,
                mayBeCut: !type.IsMax && l.Length + r.Length > type.Type.MostLength),
        };
    }

    // ---- arithmetic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>a + b</c>, <c>a - b</c>, <c>a * b</c>, <c>a / b</c>, <c>a % b</c>: string + (<see cref="Plus"/>), or numbers and dates
    /// typed by <see cref="Arithmetic.TypeOf"/>, null when an operand is, and NULL under a caller's ANSI_WARNINGS and
    /// ARITHABORT off when it may overflow or divide by zero (<see cref="Arithmetic.MayOverflow"/>, which reads a literal
    /// divisor), or under NUMERIC_ROUNDABORT on too when it may lose precision (<see cref="Arithmetic.Rounds"/>). A string with
    /// a number is not modelled yet.
    /// </summary>
    private Value Binary(BinaryExpression binary, ReadScope scope)
    {
        var op = binary.BinaryExpressionType switch
        {
            BinaryExpressionType.Add => '+',
            BinaryExpressionType.Subtract => '-',
            BinaryExpressionType.Multiply => '*',
            BinaryExpressionType.Divide => '/',
            BinaryExpressionType.Modulo => '%',
            _ => '?',
        };
        if (Strip(binary.FirstExpression) is NullLiteral || Strip(binary.SecondExpression) is NullLiteral)
            return NotModelled(binary, "this operator is not modelled yet.");
        if (op == '?')
            return BitwiseBinary(binary, scope);

        // T-SQL's unary minus and plus bind more loosely than *, / and %, so -a * b is -(a * b) on the server, where the parser
        // gives (-a) * b; and a sign inside a chain takes in what follows, a * -b * c being a * -(b * c). The type of the product
        // is the server's grouping's (observed: A_sign_before_a_product_negates_the_product).
        if (op is '*' or '/' or '%')
        {
            if (binary.FirstExpression is UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } sign)
                return SignedProduct(binary, sign, op, scope);
            if (binary.FirstExpression is BinaryExpression { BinaryExpressionType: BinaryExpressionType.Multiply or BinaryExpressionType.Divide or BinaryExpressionType.Modulo, SecondExpression: UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } })
                return NotModelled(binary, "a sign inside a chain of *, / and % takes in the rest of the chain on the server, which is not modelled.");
        }

        return Operation(binary, binary.FirstExpression, binary.SecondExpression, op, scope);
    }

    /// <summary>
    /// <c>-a * b</c>, <c>-a / b</c> or <c>-a % b</c> as the server groups it, <c>-(a * b)</c>: the product of <c>a</c> and
    /// <c>b</c>, negated (<see cref="Negated"/>). A sign that negates nothing, <c>+</c>, leaves the product as it is.
    /// </summary>
    private Value SignedProduct(BinaryExpression binary, UnaryExpression sign, char op, ReadScope scope)
    {
        if (Strip(sign.Expression) is UnaryExpression)
            return NotModelled(binary, "a value signed more than once before a product is not modelled.");
        var product = Operation(binary, sign.Expression, binary.SecondExpression, op, scope);
        return sign.UnaryExpressionType == UnaryExpressionType.Positive || product.Shape is UnknownShape ? product : Negated(product, binary);
    }

    /// <summary>
    /// <c>first op second</c>, typed at <paramref name="binary"/>: string + (<see cref="Plus"/>), or numbers and dates typed by
    /// <see cref="Arithmetic.TypeOf"/>.
    /// </summary>
    private Value Operation(BinaryExpression binary, ScalarExpression first, ScalarExpression second, char op, ReadScope scope)
    {
        var left = Expression(first, scope);
        var right = Expression(second, scope);
        if (left.Shape is UnknownShape)
            return left;
        if (right.Shape is UnknownShape)
            return right;
        // Live JSON built as text is nvarchar(max) beside a string (Typer.JsonAssembled.cs).
        if (op == '+' && LiveText(left, first) is var lj && LiveText(right, second) is var rj && (lj is not null || rj is not null)
            && (lj ?? TypeOf(left)) is { IsString: true } lt && (rj ?? TypeOf(right)) is { IsString: true } rt)
        {
            return Plus(binary, left, right, lt, rt);
        }
        // NULLIF over a literal whose fold isn't worked out here may count its literal's digits or its type's (observed:
        // Nullif_of_two_literals_is_folded_inside_arithmetic_but_not_beside_other_values).
        if (NullIfMayFold(first) || NullIfMayFold(second))
            return NotModelled(binary, "its type depends on whether SQL Server folds an operand to a constant, whose digits it would count.");
        if (OperandOf(left, first, arithmetic: true) is not { } l || OperandOf(right, second, arithmetic: true) is not { } r)
            return NotModelled(binary, "an operand of this type is not modelled yet.");
        if (op == '+' && ((l.Type.IsString && r.Type.IsString) || (l.Type.IsBinary && r.Type.IsBinary)))
            return Plus(binary, left, right, l.Type, r.Type);
        if (Arithmetic.TypeOf(op, l, r) is not { } type)
            return NotModelled(binary, "arithmetic on these types is not modelled yet.");

        // An int the server may fold to a literal counts that literal's digits (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal); when its fewest digits give another scale, not modelled.
        var mayOverflow = Arithmetic.MayOverflow(op, l, r, NumberOf(second));
        var rounds = Arithmetic.Rounds(op, l, r);
        if (Uncertain(left) || Uncertain(right))
        {
            // Never null: both operands were found above, and one by its fewest digits is found with it (OperandOf).
            var (lf, rf) = (OperandOf(left, first, least: true, arithmetic: true)!, OperandOf(right, second, least: true, arithmetic: true)!);
            if (Arithmetic.TypeOf(op, lf, rf) is not { } fewest || fewest.Scale != type.Scale || fewest.Type != type.Type)
                return NotModelled(binary, "its type depends on whether SQL Server folds an operand to a constant, whose digits it would count.");
            mayOverflow |= Arithmetic.MayOverflow(op, lf, rf, NumberOf(second));
            rounds |= Arithmetic.Rounds(op, lf, rf);
        }

        var because = NullReasons.Join(left.NullableBecause, right.NullableBecause);
        return Typed(type, NullReasons.Join(because, [.. mayOverflow ? Overflow(binary) : [], .. rounds ? PrecisionLoss(binary) : []]), binary);
    }

    /// <summary>The value of an integer or decimal literal, through parentheses and signs; null for anything else.</summary>
    private static decimal? NumberOf(ScalarExpression expression) =>
        LiteralOf(expression) is ({ } literal and (IntegerLiteral or NumericLiteral), var negative)
        && decimal.TryParse(literal.Value, System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? negative ? -value : value
            : null;

    /// <summary>
    /// <c>-a</c> and <c>+a</c>: a's type, but a tinyint negated is a smallint (observed:
    /// Arithmetic_over_float_real_money_and_dates_follows_data_type_precedence). Negating an int, smallint, bigint, money or
    /// smallmoney overflows at its least value; a literal, a decimal, a float, a real, a tinyint and <c>+</c> cannot.
    /// </summary>
    private Value Unary(UnaryExpression unary, ReadScope scope)
    {
        if (unary.UnaryExpressionType == UnaryExpressionType.BitwiseNot)
            return BitwiseNot(unary, scope);
        // A literal signed twice, -(-2147483648), is numeric(38,0) on the server, whichever way the parser splits the signs
        // (observed, A_literals_type_is_its_own_digits_or_characters), and is not modelled.
        if (Strip(unary.Expression) is UnaryExpression && LiteralOf(unary) is not null)
            return NotModelled(unary, "a literal signed more than once is not modelled.");

        // A negated literal is the literal's type, and cannot overflow; but negated, 2147483648 is int's least value, an int
        // (observed: A_literals_type_is_its_own_digits_or_characters).
        if (LiteralOf(unary) is ({ } literal, true) && literal is IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral)
        {
            // The parser gives -2147483648 as one signed literal; negating that again is numeric(38,0) on the server
            // (observed, same test), not the literal's type, and is not modelled.
            if (literal.Value.StartsWith('-') || literal.Value.StartsWith('+'))
                return NotModelled(unary, "negating a signed literal is not modelled.");
            // The parser gives 2147483648 as a numeric literal, being beyond int. An int literal negated counts the digits the
            // server counts for it, which are not always its own (IntDigits.CountOf).
            return literal.Value.TrimStart('0') == "2147483648" ? Typed(ScalarType.Of(BuiltInType.Int), [], unary)
                : literal is IntegerLiteral integer ? Integer(integer, negative: true)
                : Expression(literal, scope);
        }

        var operand = Expression(unary.Expression, scope);
        if (operand.Shape is UnknownShape || unary.UnaryExpressionType == UnaryExpressionType.Positive)
            return operand;
        return Negated(operand, unary);
    }

    /// <summary>
    /// <paramref name="operand"/> negated, at <paramref name="at"/>: its type, but a tinyint negated is a smallint, and an integer
    /// or money may overflow at its least value.
    /// </summary>
    private Value Negated(Value operand, ScalarExpression at) => TypeOf(operand) switch
    {
        ({ IsDecimal: true } or { IsApproximate: true }) and var exact => Typed(exact, operand.NullableBecause, at),
        { } tiny when tiny.Type == BuiltInType.TinyInt => Typed(ScalarType.Of(BuiltInType.SmallInt), operand.NullableBecause, at),
        ({ IsInteger: true } or { IsMoney: true }) and var integer => Typed(integer, NullReasons.Join(operand.NullableBecause, Overflow(at)), at),
        _ => NotModelled(at, "negating a value of this type is not modelled yet."),
    };

    // ---- SUM --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>SUM(x)</c>: int for an integer narrower than bigint, bigint, decimal(38, s), money, float (observed:
    /// Sum_widens_its_argument). NULL over no rows without GROUP BY, and when every value is NULL. It can overflow, which is
    /// NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed for int), except a decimal of at most 19 digits: fewer
    /// than 2^63 values below 10^(p - s) each sum to less than 10^(p - s + 19) ≤ 10^(38 - s), and no query reads 2^63 rows
    /// (a billion a second would take 292 years). HAVING comparing it proves it, as for any aggregate.
    /// </summary>
    private Value Sum(FunctionCall call, ReadScope scope)
    {
        var argument = Expression(call.Parameters[0], scope.InAggregate());
        if (argument.Shape is UnknownShape)
            return argument;
        var type = TypeOf(argument) is { } of ? SumType(of) : null;
        if (type is null)
            return NotModelled(call, "SUM of a value of this type is not modelled yet.");
        if (ProvedAggregate(call, scope))
            return Typed(type, [], call);

        var from = TypeOf(argument);
        EquatableArray<IrNullReason> because = [.. argument.NullableBecause, .. Aggregate(call, scope)];
        return Typed(type, from is { IsDecimal: true, Precision: <= 19 } ? because : NullReasons.Join(because, Overflow(call)), call);
    }

    /// <summary>The type SUM gives a value of this type, over a window too (observed: Sum_widens_its_argument).</summary>
    private static ScalarType? SumType(ScalarType of) => of switch
    {
        { IsInteger: true } when of.Type == BuiltInType.BigInt => ScalarType.Of(BuiltInType.BigInt),
        { IsInteger: true } => ScalarType.Of(BuiltInType.Int),
        { IsDecimal: true } d => new ScalarType(d.Type, Precision: 38, Scale: d.Scale),
        { IsMoney: true } => ScalarType.Of(BuiltInType.Money),
        { IsApproximate: true } => ScalarType.Of(BuiltInType.Float),
        _ => null,
    };

    // ---- assignment ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A value converted to a declared type, as an assignment to a variable or an argument to its parameter converts it: NULL
    /// too under a caller's ANSI_WARNINGS and ARITHABORT off when it may overflow (observed for CAST, which is the same
    /// conversion), unless <paramref name="fits"/> says a literal fits; and when it may be rounded to fewer places, under
    /// NUMERIC_ROUNDABORT on as well (observed: A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off,
    /// for SET, SELECT, a declaration's value, FETCH INTO, UPDATE and an inline function's argument).
    /// </summary>
    /// <param name="expression">The expression the value was read from, when there is one, for a string literal's places.</param>
    /// <param name="fits">Whether the value is a literal known to fit the type, so that it cannot overflow.</param>
    internal static EquatableArray<IrNullReason> Converting(Value value, Shape declared, SourceLocation at, SessionOptions session, ScalarExpression? expression = null, bool fits = false)
    {
        if (value.Shape is UnknownShape || value.IsLiveJson || declared is not ScalarShape { BuiltIn: { } to })
            return value.NullableBecause;
        var converts = fits || (TypeOf(value) is { } from && Conversions.Converts(Operand.OfType(from), to));
        return NullReasons.Join(value.NullableBecause, ConversionReasons(session, value, expression, to, converts, at));
    }
}
