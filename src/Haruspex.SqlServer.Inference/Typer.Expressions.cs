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
            ? new Value(SqlTypes.ToConvertedShape(type, At(literal)), [])
            : Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"The literal {Text(literal)} is not ASCII, so its length in bytes depends on the code page.", At(literal)));

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

    /// <summary>The scalar type of a value, when it is a built-in scalar.</summary>
    private static ScalarType? TypeOf(Value value)
    {
        if (value.IsLiveJson)
            return null;
        return value.Shape switch
        {
            ScalarShape { BuiltIn: var type } => type,
            JsonTextShape => new ScalarType(BuiltInType.NVarChar, -1),
            _ => null,
        };
    }

    /// <summary>
    /// A value as an operand of a type rule: its type, and the integer digits the server counts for it. An integer literal
    /// counts its own, and so does an int the server treats as a constant (<see cref="Value.Digits"/>): its fewest when
    /// <paramref name="least"/>, else its most. Anything else its type's.
    /// </summary>
    private static Operand? OperandOf(Value value, ScalarExpression expression, bool least = false)
    {
        if (TypeOf(value) is not { } type)
            return null;
        if (IntegerLiteralOf(expression) is { } literal)
            return new Operand(type, literal.Value.TrimStart('-', '+').TrimStart('0').Length is var n and > 0 ? n : 1);
        if (type.Type == BuiltInType.Int && (least ? value.Digits.Least : value.Digits.Most) is var d and < 10)
            return new Operand(type, d);
        return Operand.OfType(type);
    }

    /// <summary>Whether the server may count an int's digits as a literal's or as its type's, depending on what it folds.</summary>
    private static bool Uncertain(Value value) => value.Shape is ScalarShape { BuiltIn.Type: var type } && type == BuiltInType.Int && !value.IsLiveJson && value.Digits.IsUncertain;

    /// <summary>An integer literal, through parentheses and a sign.</summary>
    private static IntegerLiteral? IntegerLiteralOf(ScalarExpression expression) => Strip(expression) switch
    {
        IntegerLiteral literal => literal,
        UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary => IntegerLiteralOf(unary.Expression),
        _ => null,
    };

    /// <summary>A literal, through parentheses, with whether it is negated.</summary>
    internal static (Literal Literal, bool Negative)? LiteralOf(ScalarExpression expression)
    {
        var negative = false;
        var e = Strip(expression);
        while (e is UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary)
        {
            negative ^= unary.UnaryExpressionType == UnaryExpressionType.Negative;
            e = Strip(unary.Expression);
        }

        return e is Literal literal ? (literal, negative) : null;
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

    private Value NotModelled(TSqlFragment expression, string why) =>
        Value.Unknown(Unsupported(IrUnknownReason.UnsupportedExpression, $"{Text(expression)}: {why}", At(expression)));

    // ---- combining types --------------------------------------------------------------------------------------------

    /// <summary>
    /// The type values of different types take when they meet in one column: a set operation's, COALESCE's, a CASE's, IIF's or
    /// CHOOSE's, by the server's type rule for a chain (<see cref="TypeChain.Of"/>), a fold from left to right. It is not the
    /// knowledge join (<see cref="Value.Join"/>), which says what is known of a value that may come from either side.
    /// </summary>
    /// <param name="Type">The type, or null when it is not known.</param>
    /// <param name="Overflows">For each value, the reason converting it may give NULL: an overflow, NULL under a caller's
    /// ANSI_WARNINGS and ARITHABORT off (observed: A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options);
    /// empty when it cannot (<see cref="Conversions.NeverNull"/>, or a literal that fits).</param>
    /// <param name="Why">When the type is not known, why, naming the types so a user knows what to change.</param>
    private sealed record ChainType(ScalarType? Type, EquatableArray<EquatableArray<IrNullReason>> Overflows, string? Why);

    /// <param name="values">Each value, the expression it came from (for a literal's digits and value), and where an overflow
    /// converting it would happen.</param>
    private ChainType Chained(IReadOnlyList<(Value Value, ScalarExpression Expression, SourceLocation At)> values)
    {
        var operands = values.Select(v => OperandOf(v.Value, v.Expression)).ToList();
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
        if (values.Any(v => Uncertain(v.Value)) && TypeChain.Of([.. values.Select(v => OperandOf(v.Value, v.Expression, least: true)!)]) is var fewest
            && (fewest is null || fewest.Type != type.Type || fewest.Scale != type.Scale))
        {
            return new ChainType(null, [], $"its type depends on whether SQL Server folds {Names(values.Where(v => Uncertain(v.Value)).Select(v => Text(v.Expression)))} to a constant, whose digits it would count.");
        }

        var overflows = values.Select((v, i) =>
        {
            var fits = (LiteralOf(v.Expression) is ({ } literal, var negative) ? Conversions.Fits(literal, type, negative) : null)
                ?? Conversions.NeverNull(known[i], type);
            return fits ? [] : Overflow(summaries.Session, v.At);
        }).ToList();
        return new ChainType(type, [.. overflows], null);
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
        if (values.Any(IsJson) || ReferenceEquals(coalesce, scope.JsonQueryArgument))
        {
            var json = CoalesceJson(coalesce, arguments, values, because, scope);
            if (values.Any(IsJson) || json.Shape is JsonTextShape)
                return json;
        }

        // A value converted to the common type may overflow it, NULL under the caller's options, whichever argument it is
        // (observed: A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options).
        var at = At(coalesce);
        var combined = Chained([.. values.Select((v, i) => (v, arguments[i], at))]);
        if (combined.Type is not { } type)
            return NotModelled(coalesce, combined.Why!); // Never null: a chain without a type says why (Chained).

        // COALESCE(NULL, 1) is folded to 1 and counts its digits (observed: An_int_constant_counts_its_own_digits_beside_a_decimal).
        return Typed(type, combined.Overflows.Aggregate(because, NullReasons.Join), coalesce) with
        {
            Digits = type.Type == BuiltInType.Int ? IntDigits.Foldable(values.Select(v => v.Digits)) : IntDigits.OfType,
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
    /// overflow, which would make it NULL under a caller's session options (observed for ISNULL(@ti, 1000)). A value never
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

        if (first.NullableBecause.Count == 0)
            return first;
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
        // An overflow converting the second is NULL, or an error that fails the call when the callers' sessions make it one;
        // then ISNULL of a second never null is never null.
        var overflow = fits ? [] : Overflow(call);
        if (second.NullableBecause.Count == 0 && overflow.Count == 0)
            return Typed(type, [], call);
        return Typed(type, NullReasons.Join(NullReasons.Join(first.NullableBecause, second.NullableBecause), overflow), call);
    }

    // ---- CAST and CONVERT -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CAST(x AS t)</c>, <c>CONVERT(t, x [, style])</c>, and their <c>TRY_</c> forms: of type t whatever the style (observed:
    /// Cast_and_convert_are_of_the_target_type_whatever_the_style), null when x is; and, unless every value of x's type
    /// converts to t without an overflow (<see cref="Conversions.Converts"/>), NULL under a caller's ANSI_WARNINGS and ARITHABORT
    /// off (<c>arithmetic-overflow</c>), or for TRY_ whenever the conversion may fail (<c>failed-conversion</c>, observed:
    /// Try_cast_and_try_convert_are_null_when_the_conversion_fails). A string type without a length is 30 long here, where a
    /// declaration's is 1 (observed: A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration). A conversion to
    /// x's own type changes nothing, so the value keeps its facts. The format a style gives is not claimed.
    /// </summary>
    private Value Cast(DataTypeReference target, ScalarExpression operand, TSqlFragment call, ReadScope scope, bool styled = false, bool attempt = false)
    {
        var type = SyntaxReader.Type(target);
        var to = SqlTypes.Modelled(SchemaRules.ResolveType(model, type)?.Scalar);
        if (to is { IsString: true } && type is { Kind: SqlTypeKind.BuiltIn, Arguments.Count: 0 })
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
        if (value.Shape is UnknownShape || value.IsLiveJson)
            return value.Shape is UnknownShape ? value : NotModelled(call, "a conversion of JSON is not modelled yet.");

        var from = OperandOf(value, operand);

        // A conversion the chart refuses is an error when the procedure is created (529), so it describes nothing that runs
        // (observed in CatalogueSweepGridTests; the chart is ConversionChart).
        if (from is not null && ConversionChart.Of(from.Type.Type, to.Type) == ConversionKind.Refused)
            return NotModelled(call, $"SQL Server refuses to convert {from.Type.Text} to {to.Text}.");
        var fits = (!styled && LiteralOf(operand) is ({ } literal, var negative) ? Conversions.Fits(literal, to, negative) : null)
            ?? (from is not null && Conversions.Converts(from, to, styled));
        // A NULL cast to int and cast again stays that constant; CAST(1 AS int) counts an int's 10 digits (same observation).
        if (fits && from?.Type.Text == to.Text && !styled)
            return value.Digits == IntDigits.Literal(1) && IsNullCast(operand) ? value : value with { Digits = IntDigits.OfType };
        var because = fits
            ? value.NullableBecause
            : NullReasons.Join(value.NullableBecause, attempt ? [new IrNullReason(IrNullRule.FailedConversion, At(call).ToString())] : Overflow(call));
        return Value.Computed(shape, because);
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
    /// 12 and a bit 1 (observed), the NULL literal nothing; null for anything else, whose length is not modelled.
    /// </summary>
    private (ScalarType Type, int Length)? Piece(ScalarExpression expression, ReadScope scope)
    {
        if (Strip(expression) is NullLiteral)
            return (new ScalarType(BuiltInType.VarChar, 0), 0);
        var value = Expression(expression, scope);
        return TypeOf(value) switch
        {
            { IsString: true } s => (s, s.Length),
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
        var pieces = arguments.Select(a => Piece(a, scope)).ToList();
        if (pieces.Any(p => p is null))
            return NotModelled(call, "an argument whose length as a string is not modelled yet.");

        var known = pieces.OfType<(ScalarType Type, int Length)>().ToList();
        var parts = separated ? [.. known.Skip(1)] : known;
        var separator = separated ? known[0].Length * (parts.Count - 1) : 0;
        var max = known.Any(p => p.Type.IsMax);
        var national = known.Any(p => p.Type.IsNational);
        return Typed(ScalarType.CharacterString(national, varying: true, max ? -1 : parts.Sum(p => p.Length) + separator), [], call);
    }

    /// <summary>
    /// String <c>a + b</c>: as CONCAT for its type, but fixed-length when both operands are (char, or nchar if either is:
    /// observed, Fixed_length_strings_plus_is_fixed_length), and null when either operand is. With
    /// CONCAT_NULL_YIELDS_NULL off, which only a session can set and is deprecated, NULL + 'x' is 'x'
    /// (With_concat_null_yields_null_off_a_string_plus_null_is_the_string), so only when both are: when the callers' sessions
    /// are declared to set it off, an operand never null makes the sum never null. Not known, the claim of either is null holds
    /// under both settings, since off is only less often null.
    /// </summary>
    private Value Plus(BinaryExpression plus, Value left, Value right, ScalarType l, ScalarType r) =>
        Typed(
            ScalarType.CharacterString(l.IsNational || r.IsNational, varying: l.IsVarying || r.IsVarying, l.IsMax || r.IsMax ? -1 : l.Length + r.Length),
            summaries.Session.ConcatenatingNullKeepsTheString && (left.NullableBecause.Count == 0 || right.NullableBecause.Count == 0)
                ? []
                : NullReasons.Join(left.NullableBecause, right.NullableBecause),
            plus);

    // ---- arithmetic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>a + b</c>, <c>a - b</c>, <c>a * b</c>, <c>a / b</c>, <c>a % b</c>: string + (<see cref="Plus"/>), or numbers and dates
    /// typed by <see cref="Arithmetic.TypeOf"/>, null when an operand is, and NULL under a caller's ANSI_WARNINGS and
    /// ARITHABORT off when it may overflow or divide by zero (<see cref="Arithmetic.MayOverflow"/>, which reads a literal
    /// divisor). A string with a number is not modelled yet.
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
        if (op == '?' || Strip(binary.FirstExpression) is NullLiteral || Strip(binary.SecondExpression) is NullLiteral)
            return NotModelled(binary, "this operator is not modelled yet.");

        var left = Expression(binary.FirstExpression, scope);
        var right = Expression(binary.SecondExpression, scope);
        if (left.Shape is UnknownShape)
            return left;
        if (right.Shape is UnknownShape)
            return right;
        if (OperandOf(left, binary.FirstExpression) is not { } l || OperandOf(right, binary.SecondExpression) is not { } r)
            return NotModelled(binary, "an operand of this type is not modelled yet.");
        if (op == '+' && l.Type.IsString && r.Type.IsString)
            return Plus(binary, left, right, l.Type, r.Type);
        if (Arithmetic.TypeOf(op, l, r) is not { } type)
            return NotModelled(binary, "arithmetic on these types is not modelled yet.");

        // An int the server may fold to a literal counts that literal's digits (observed:
        // An_int_constant_counts_its_own_digits_beside_a_decimal); when its fewest digits give another scale, not modelled.
        var mayOverflow = Arithmetic.MayOverflow(op, l, r, NumberOf(binary.SecondExpression));
        if (Uncertain(left) || Uncertain(right))
        {
            // Never null: both operands were found above, and one by its fewest digits is found with it (OperandOf).
            var (lf, rf) = (OperandOf(left, binary.FirstExpression, least: true)!, OperandOf(right, binary.SecondExpression, least: true)!);
            if (Arithmetic.TypeOf(op, lf, rf) is not { } fewest || fewest.Scale != type.Scale || fewest.Type != type.Type)
                return NotModelled(binary, "its type depends on whether SQL Server folds an operand to a constant, whose digits it would count.");
            mayOverflow |= Arithmetic.MayOverflow(op, lf, rf, NumberOf(binary.SecondExpression));
        }

        var because = NullReasons.Join(left.NullableBecause, right.NullableBecause);
        return Typed(type, mayOverflow ? NullReasons.Join(because, Overflow(binary)) : because, binary);
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
        if (unary.UnaryExpressionType is not (UnaryExpressionType.Negative or UnaryExpressionType.Positive))
            return NotModelled(unary, "this operator is not modelled yet.");
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
            // The parser gives 2147483648 as a numeric literal, being beyond int.
            return literal.Value.TrimStart('0') == "2147483648"
                ? Typed(ScalarType.Of(BuiltInType.Int), [], unary)
                : Expression(literal, scope);
        }

        var operand = Expression(unary.Expression, scope);
        if (operand.Shape is UnknownShape || unary.UnaryExpressionType == UnaryExpressionType.Positive)
            return operand;
        return TypeOf(operand) switch
        {
            ({ IsDecimal: true } or { IsApproximate: true }) and var exact => Typed(exact, operand.NullableBecause, unary),
            { } tiny when tiny.Type == BuiltInType.TinyInt => Typed(ScalarType.Of(BuiltInType.SmallInt), operand.NullableBecause, unary),
            ({ IsInteger: true } or { IsMoney: true }) and var integer => Typed(integer, NullReasons.Join(operand.NullableBecause, Overflow(unary)), unary),
            _ => NotModelled(unary, "negating a value of this type is not modelled yet."),
        };
    }

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
    /// A value converted to a declared type, as an assignment to a variable converts it: NULL too under a caller's
    /// ANSI_WARNINGS and ARITHABORT off when it may overflow (observed for CAST, which is the same conversion).
    /// </summary>
    internal static EquatableArray<IrNullReason> Converting(Value value, Shape declared, SourceLocation at, SessionOptions session)
    {
        if (value.Shape is UnknownShape || value.IsLiveJson || declared is not ScalarShape { BuiltIn: { } to })
            return value.NullableBecause;
        var from = value.Shape switch
        {
            ScalarShape { BuiltIn: var s } => s,
            JsonTextShape => new ScalarType(BuiltInType.NVarChar, -1),
            _ => null,
        };
        return from is not null && Conversions.Converts(Operand.OfType(from), to)
            ? value.NullableBecause
            : NullReasons.Join(value.NullableBecause, Overflow(session, at));
    }
}
