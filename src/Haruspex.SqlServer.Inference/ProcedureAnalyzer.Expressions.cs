using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Slice 6f: the common expressions. Types come from ScalarTypes.cs; README.md in this folder ("Expressions") gives the rules
// and the observations they rest on (ExpressionObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
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
            ? new Value(SqlTypes.ToConvertedShape(model, type.ToSqlType(), At(literal)), [])
            : Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The literal {Text(literal)} is not ASCII, so its length in bytes depends on the code page.", At(literal)));

    private static ScalarType? LiteralType(StringLiteral literal) =>
        literal.Collation is not null || (!literal.IsNational && literal.Value.Any(c => c > '\u007f'))
            ? null
            : new ScalarType(literal.IsNational ? "nvarchar" : "varchar", Math.Max(1, literal.Value.Length));

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
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The literal {literal.Value} has more than 38 digits.", At(literal)));
        return new Value(SqlTypes.ToConvertedShape(model, new ScalarType("numeric", Precision: precision, Scale: scale).ToSqlType(), At(literal)), []);
    }

    /// <summary>The scalar type of a value, when it is a built-in scalar; an integer literal keeps its digits.</summary>
    private static ScalarType? TypeOf(Value value, ScalarExpression expression)
    {
        if (value.IsLiveJson)
            return null;
        var type = value.Shape switch
        {
            ScalarShape { Type.Sql: var sql } => ScalarType.Parse(sql),
            JsonTextShape => new ScalarType("nvarchar", -1),
            _ => null,
        };
        return type is not null && IntegerLiteralOf(expression) is { } literal ? type with { Digits = literal.Value.TrimStart('-', '+').TrimStart('0').Length is var n and > 0 ? n : 1 } : type;
    }

    /// <summary>An integer literal, through parentheses and a sign.</summary>
    private static IntegerLiteral? IntegerLiteralOf(ScalarExpression expression) => Strip(expression) switch
    {
        IntegerLiteral literal => literal,
        UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative or UnaryExpressionType.Positive } unary => IntegerLiteralOf(unary.Expression),
        _ => null,
    };

    /// <summary>A literal, through parentheses, with whether it is negated.</summary>
    private static (Literal Literal, bool Negative)? LiteralOf(ScalarExpression expression)
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

    private Value Typed(ScalarType type, IReadOnlyList<IrNullReason> because, TSqlFragment at) =>
        new(SqlTypes.ToConvertedShape(model, type.ToSqlType(), At(at)), because);

    private IrNullReason Overflow(TSqlFragment at) => new(IrNullRules.ArithmeticOverflow, At(at).ToString());

    private Value NotModelled(TSqlFragment expression, string why) =>
        Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"{Text(expression)}: {why}", At(expression)));

    // ---- COALESCE and ISNULL ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>COALESCE(a, b, …)</c>: the type of highest precedence (<see cref="ScalarType.Union"/>), to which every argument
    /// converts without an overflow. It is <c>CASE WHEN a IS NOT NULL THEN a …</c>, so it is never null when an argument is
    /// never null, except that an argument before it that is a subquery or a call is read twice and may be NULL the second
    /// time (observed: Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot), which is <c>read-twice</c>: a value
    /// tested NULL is passed over, so only the second read can make it NULL. No seed reaches it, so it has a rule of its own.
    /// A NULL literal adds nothing but its place.
    /// </summary>
    private Value Coalesce(CoalesceExpression coalesce, BindingScope scope)
    {
        var arguments = coalesce.Expressions.Where(e => Strip(e) is not NullLiteral).ToList();
        if (arguments.Count == 0)
            return NotModelled(coalesce, "COALESCE of NULL only is an error.");
        var values = arguments.Select(a => Expression(a, scope)).ToList();
        if (values.FirstOrDefault(v => v.Shape is UnknownShape) is { } unknown)
            return unknown;
        var first = values.FindIndex(v => v.NullableBecause.Count == 0);
        var because = first < 0
            ? values.Select(v => v.NullableBecause).Aggregate(ShapeJoin.Reasons)
            : values.Take(first).Select((v, i) => i).Where(i => ReadTwice(arguments[i]))
                .Select(i => (IReadOnlyList<IrNullReason>)[new IrNullReason(IrNullRules.ReadTwice, At(arguments[i]).ToString())])
                .Aggregate((IReadOnlyList<IrNullReason>)[], ShapeJoin.Reasons);
        if (values.Any(IsJson))
            return CoalesceJson(coalesce, arguments, values, because);

        var types = values.Select((v, i) => TypeOf(v, arguments[i])).ToList();
        if (types.Any(t => t is null) || ScalarType.Union(types!) is not { } type)
            return NotModelled(coalesce, "COALESCE of these types is not modelled yet.");
        return Typed(type, because, coalesce);
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
        private static readonly HashSet<string> Pure = new(StringComparer.OrdinalIgnoreCase) { "isnull", "concat", "concat_ws", "choose" };

        public bool Found { get; private set; }

        public override void Visit(ScalarSubquery node) => Found = true;

        public override void Visit(FunctionCall node) =>
            Found |= !(node.CallTarget is null && node.OverClause is null && (Pure.Contains(node.FunctionName.Value) || IsAggregate(node)));
    }

    /// <summary>
    /// <c>ISNULL(a, b)</c>: a's type, to which b is converted, a longer string cut short (observed:
    /// Isnull_converts_its_second_argument_to_the_firsts_type_cutting_a_string_short); b's type when a is the NULL literal. It
    /// reads a once, so it is never null when a is never null; nor when b is never null and converts to a's type without an
    /// overflow, which would make it NULL under a caller's session options (observed for ISNULL(@ti, 1000)). A value never
    /// null is a itself, with its facts; otherwise only a's type's facts hold, since b may be outside a's CHECK set.
    /// </summary>
    private Value IsNull(FunctionCall call, BindingScope scope)
    {
        var (a, b) = (call.Parameters[0], call.Parameters[1]);
        if (Strip(a) is NullLiteral)
            return Strip(b) is NullLiteral ? NotModelled(call, "ISNULL of NULL and NULL is not typed.") : Expression(b, scope);
        var first = Expression(a, scope);
        if (first.Shape is UnknownShape || first.NullableBecause.Count == 0)
            return first;
        if (IsJson(first))
            return Strip(b) is NullLiteral ? first : IsNullJson(call, a, first, b, scope);
        if (TypeOf(first, a) is not { } type)
            return NotModelled(call, "ISNULL of a value of this type is not modelled yet.");
        if (Strip(b) is NullLiteral)
            return Typed(type, first.NullableBecause, call);

        var second = Expression(b, scope);
        if (second.Shape is UnknownShape)
            return second;
        var fits = (LiteralOf(b) is ({ } literal, var negative) ? ScalarType.Fits(literal, type, negative) : null)
            ?? (TypeOf(second, b) is { } from && ScalarType.Converts(from, type));
        if (second.NullableBecause.Count == 0 && fits)
            return Typed(type, [], call);
        var because = ShapeJoin.Reasons(first.NullableBecause, second.NullableBecause);
        return Typed(type, fits ? because : ShapeJoin.Reasons(because, [Overflow(call)]), call);
    }

    // ---- CAST and CONVERT -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CAST(x AS t)</c>, <c>CONVERT(t, x [, style])</c>, and their <c>TRY_</c> forms: of type t whatever the style (observed:
    /// Cast_and_convert_are_of_the_target_type_whatever_the_style), null when x is; and, unless every value of x's type
    /// converts to t without an overflow (<see cref="ScalarType.Converts"/>), NULL under a caller's ANSI_WARNINGS and ARITHABORT
    /// off (<c>arithmetic-overflow</c>), or for TRY_ whenever the conversion may fail (<c>failed-conversion</c>, observed:
    /// Try_cast_and_try_convert_are_null_when_the_conversion_fails). A string type without a length is 30 long here, where a
    /// declaration's is 1 (observed: A_string_type_without_a_length_is_30_long_in_a_cast_and_1_in_a_declaration). A conversion to
    /// x's own type changes nothing, so the value keeps its facts. The format a style gives is not claimed.
    /// </summary>
    private Value Cast(DataTypeReference target, ScalarExpression operand, TSqlFragment call, BindingScope scope, bool styled = false, bool attempt = false)
    {
        var type = SqlType.From(target);
        if (type is { Kind: SqlTypeKind.BuiltIn, Name: "char" or "varchar" or "nchar" or "nvarchar", Arguments.Count: 0 })
            type = new SqlType(SqlTypeKind.BuiltIn, type.Name, ["30"]);
        var to = ScalarType.Parse(SchemaRules.ResolveType(model, type)?.BuiltIn.ToString());
        var shape = to is null ? null : SqlTypes.ToConvertedShape(model, to.ToSqlType(), At(call));
        if (to is null || shape is null or UnknownShape)
            return NotModelled(call, "a conversion to this type is not modelled yet.");

        var value = Expression(operand, scope);
        if (value.Shape is UnknownShape || value.IsLiveJson)
            return value.Shape is UnknownShape ? value : NotModelled(call, "a conversion of JSON is not modelled yet.");

        var from = TypeOf(value, operand);
        var fits = (!styled && LiteralOf(operand) is ({ } literal, var negative) ? ScalarType.Fits(literal, to, negative) : null)
            ?? (from is not null && ScalarType.Converts(from, to, styled));
        if (fits && from?.Text == to.Text && !styled)
            return value;
        IReadOnlyList<IrNullReason> because = fits
            ? value.NullableBecause
            : ShapeJoin.Reasons(value.NullableBecause, [attempt ? new IrNullReason(IrNullRules.FailedConversion, At(call).ToString()) : Overflow(call)]);
        return new Value(shape, because);
    }

    // ---- strings --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What a value adds to a string built by CONCAT: a string its length (bytes for varchar, characters for nvarchar), an int
    /// 12 and a bit 1 (observed), the NULL literal nothing; null for anything else, whose length is not modelled.
    /// </summary>
    private (ScalarType Type, int Length)? Piece(ScalarExpression expression, BindingScope scope)
    {
        if (Strip(expression) is NullLiteral)
            return (new ScalarType("varchar", 0), 0);
        var value = Expression(expression, scope);
        return TypeOf(value, expression) switch
        {
            { IsString: true } s => (s, s.Length),
            { Name: "int" } i => (i, 12),
            { Name: "bit" } b => (b, 1),
            _ => null,
        };
    }

    /// <summary>
    /// <c>CONCAT(a, b, …)</c> and <c>CONCAT_WS(sep, a, b, …)</c>: never null, whatever is (a NULL counts as empty); nvarchar if
    /// any argument is, the separator included; as long as the arguments together, plus the separator between each two; max if
    /// any is, otherwise cut at 8000 bytes, 4000 characters for nvarchar (observed:
    /// Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null).
    /// </summary>
    private Value Concat(FunctionCall call, BindingScope scope, bool separated)
    {
        var arguments = call.Parameters.ToList();
        if (arguments.Count < (separated ? 3 : 2))
            return NotModelled(call, "too few arguments.");
        var pieces = arguments.Select(a => Piece(a, scope)).ToList();
        if (pieces.Any(p => p is null))
            return NotModelled(call, "an argument whose length as a string is not modelled yet.");

        var parts = separated ? pieces.Skip(1).ToList() : pieces;
        var separator = separated ? pieces[0]!.Value.Length * (parts.Count - 1) : 0;
        var max = pieces.Any(p => p!.Value.Type.IsMax);
        var national = pieces.Any(p => p!.Value.Type.IsNational);
        return Typed(ScalarType.String(national, varying: true, max ? -1 : parts.Sum(p => p!.Value.Length) + separator), [], call);
    }

    /// <summary>
    /// String <c>a + b</c>: as CONCAT for its type, but fixed-length when both operands are (char, or nchar if either is:
    /// observed, Fixed_length_strings_plus_is_fixed_length), and null when either operand is. With
    /// CONCAT_NULL_YIELDS_NULL off, which only a session can set and is deprecated, NULL + 'x' is 'x'
    /// (With_concat_null_yields_null_off_a_string_plus_null_is_the_string): less often null, so the claim holds either way.
    /// </summary>
    private Value Plus(BinaryExpression plus, Value left, Value right, ScalarType l, ScalarType r) =>
        Typed(
            ScalarType.String(l.IsNational || r.IsNational, varying: l.Name.EndsWith("varchar", StringComparison.Ordinal) || r.Name.EndsWith("varchar", StringComparison.Ordinal), l.IsMax || r.IsMax ? -1 : l.Length + r.Length),
            ShapeJoin.Reasons(left.NullableBecause, right.NullableBecause),
            plus);

    // ---- arithmetic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>a + b</c>, <c>a - b</c>, <c>a * b</c>, <c>a / b</c>: string + (<see cref="Plus"/>), or exact numbers typed by
    /// <see cref="ScalarType.Arithmetic"/>, null when an operand is. Integers can overflow, and division can divide by zero,
    /// either NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed); a decimal's +, - and * cannot, within 38
    /// digits. Anything else (money, float, dates, a string with a number) is not modelled yet.
    /// </summary>
    private Value Binary(BinaryExpression binary, BindingScope scope)
    {
        var op = binary.BinaryExpressionType switch
        {
            BinaryExpressionType.Add => '+',
            BinaryExpressionType.Subtract => '-',
            BinaryExpressionType.Multiply => '*',
            BinaryExpressionType.Divide => '/',
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
        if (TypeOf(left, binary.FirstExpression) is not { } l || TypeOf(right, binary.SecondExpression) is not { } r)
            return NotModelled(binary, "an operand of this type is not modelled yet.");
        if (op == '+' && l.IsString && r.IsString)
            return Plus(binary, left, right, l, r);
        if (ScalarType.Arithmetic(op, l, r) is not { } type)
            return NotModelled(binary, "arithmetic on these types is not modelled yet.");

        var because = ShapeJoin.Reasons(left.NullableBecause, right.NullableBecause);
        return Typed(type, type.IsInteger || op == '/' ? ShapeJoin.Reasons(because, [Overflow(binary)]) : because, binary);
    }

    /// <summary>
    /// <c>-a</c> and <c>+a</c>: a's type. Negating an integer overflows at its least value; a literal, a decimal and <c>+</c>
    /// cannot.
    /// </summary>
    private Value Unary(UnaryExpression unary, BindingScope scope)
    {
        if (unary.UnaryExpressionType is not (UnaryExpressionType.Negative or UnaryExpressionType.Positive))
            return NotModelled(unary, "this operator is not modelled yet.");
        // A literal signed twice, -(-2147483648), is numeric(38,0) on the server, whichever way the parser splits the signs
        // (observed, A_literals_type_is_its_own_digits_or_characters), and is not modelled (review round 2).
        if (Strip(unary.Expression) is UnaryExpression && LiteralOf(unary) is not null)
            return NotModelled(unary, "a literal signed more than once is not modelled.");

        // A negated literal is the literal's type, and cannot overflow; but negated, 2147483648 is int's least value, an int
        // (observed: A_literals_type_is_its_own_digits_or_characters).
        if (LiteralOf(unary) is ({ } literal, true) && literal is IntegerLiteral or NumericLiteral)
        {
            // The parser gives -2147483648 as one signed literal; negating that again is numeric(38,0) on the server
            // (observed, same test), not the literal's type, and is not modelled (review round 2).
            if (literal.Value.StartsWith('-') || literal.Value.StartsWith('+'))
                return NotModelled(unary, "negating a signed literal is not modelled.");
            // The parser gives 2147483648 as a numeric literal, being beyond int.
            return literal.Value.TrimStart('0') == "2147483648"
                ? Typed(new ScalarType("int"), [], unary)
                : Expression(literal, scope);
        }

        var operand = Expression(unary.Expression, scope);
        if (operand.Shape is UnknownShape || unary.UnaryExpressionType == UnaryExpressionType.Positive)
            return operand;
        return TypeOf(operand, unary.Expression) switch
        {
            { IsDecimal: true } decimalType => Typed(decimalType, operand.NullableBecause, unary),
            { IsInteger: true } integer => Typed(integer, ShapeJoin.Reasons(operand.NullableBecause, [Overflow(unary)]), unary),
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
    private Value Sum(FunctionCall call, BindingScope scope)
    {
        var argument = InAggregate(() => Expression(call.Parameters[0], scope));
        if (argument.Shape is UnknownShape)
            return argument;
        var type = TypeOf(argument, call.Parameters[0]) switch
        {
            { Name: "tinyint" or "smallint" or "int" } => new ScalarType("int"),
            { Name: "bigint" } => new ScalarType("bigint"),
            { IsDecimal: true } d => new ScalarType(d.Name, Precision: 38, Scale: d.Scale),
            { Name: "money" or "smallmoney" } => new ScalarType("money"),
            { Name: "float" or "real" } => new ScalarType("float"),
            _ => null,
        };
        if (type is null)
            return NotModelled(call, "SUM of a value of this type is not modelled yet.");
        if (ProvedAggregate(call))
            return Typed(type, [], call);

        var from = TypeOf(argument, call.Parameters[0])!;
        IReadOnlyList<IrNullReason> because = [.. argument.NullableBecause, .. Aggregate(call)];
        return Typed(type, from is { IsDecimal: true, Precision: <= 19 } ? because : ShapeJoin.Reasons(because, [Overflow(call)]), call);
    }

    // ---- assignment ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A value converted to a declared type, as an assignment to a variable converts it: NULL too under a caller's
    /// ANSI_WARNINGS and ARITHABORT off when it may overflow (observed for CAST, which is the same conversion).
    /// </summary>
    private static IReadOnlyList<IrNullReason> Converting(Value value, Shape declared, SourceLocation at)
    {
        if (value.Shape is UnknownShape || value.IsLiveJson || declared is not ScalarShape { Type.Sql: var sql } || ScalarType.Parse(sql) is not { } to)
            return value.NullableBecause;
        var from = value.Shape switch
        {
            ScalarShape { Type.Sql: var s } => ScalarType.Parse(s),
            JsonTextShape => new ScalarType("nvarchar", -1),
            _ => null,
        };
        return from is not null && ScalarType.Converts(from, to)
            ? value.NullableBecause
            : ShapeJoin.Reasons(value.NullableBecause, [new IrNullReason(IrNullRules.ArithmeticOverflow, at.ToString())]);
    }
}
