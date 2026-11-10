using System.Globalization;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A built-in scalar type as SQL Server types an expression (slice 6f): what <c>COALESCE</c>, conversions, string building and
/// decimal arithmetic compute from, and what they give. Each rule here was observed (the differential tests'
/// <c>ExpressionObservationTests</c>, named on each), and anything not listed gives null, which the caller makes unknown.
/// </summary>
/// <param name="Name">The built-in name in lower case.</param>
/// <param name="Length">A string's length (bytes for char and varchar, characters for nchar and nvarchar); -1 for max.</param>
/// <param name="Precision">An exact number's precision, or a date or time type's fractional digits.</param>
/// <param name="Scale">An exact number's scale.</param>
/// <param name="Digits">
/// For an integer literal, its own digits: beside a decimal it counts as decimal(digits, 0), where an int column or variable
/// counts as decimal(10, 0) (observed: <c>Coalesce_takes_the_type_of_highest_precedence_and_isnull_the_first_arguments</c>).
/// </param>
internal sealed record ScalarType(string Name, int Length = 0, int Precision = 0, int Scale = 0, int? Digits = null)
{
    /// <summary>The longest a varchar can be without max, in bytes; nvarchar's is half, in characters.</summary>
    public const int MostBytes = 8000;

    public bool IsString => Name is "char" or "varchar" or "nchar" or "nvarchar";

    public bool IsNational => Name is "nchar" or "nvarchar";

    public bool IsMax => Length < 0;

    public bool IsInteger => Name is "tinyint" or "smallint" or "int" or "bigint";

    public bool IsDecimal => Name is "decimal" or "numeric";

    /// <summary>A number with an exact decimal form: an integer, bit, decimal, numeric, money or smallmoney.</summary>
    public bool IsExact => IsInteger || IsDecimal || Name is "bit" or "money" or "smallmoney";

    public bool IsDateOrTime => Name is "date" or "time" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset";

    /// <summary>The type as the IR writes it, <c>decimal(19,2)</c>.</summary>
    public string Text => Name switch
    {
        _ when IsString => $"{Name}({(IsMax ? "max" : Length.ToString(CultureInfo.InvariantCulture))})",
        _ when IsDecimal => $"{Name}({Precision},{Scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{Name}({Precision})",
        _ => Name,
    };

    public SqlType ToSqlType() => Text.IndexOf('(', StringComparison.Ordinal) is var open and >= 0
        ? new SqlType(SqlTypeKind.BuiltIn, Name, Text[(open + 1)..^1].Split(','))
        : new SqlType(SqlTypeKind.BuiltIn, Name, []);

    /// <summary>A built-in type as the IR writes it (<c>decimal(19,2)</c>, <c>nvarchar(max)</c>), with the defaults of a declaration.</summary>
    public static ScalarType? Parse(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
            return null;
        var open = sql.IndexOf('(', StringComparison.Ordinal);
        var name = open < 0 ? sql : sql[..open];
        var arguments = open < 0 ? [] : sql[(open + 1)..^1].Split(',');
        int Argument(int i, int otherwise) =>
            arguments.Length > i && int.TryParse(arguments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : otherwise;

        return name switch
        {
            "char" or "varchar" or "nchar" or "nvarchar" => new ScalarType(name, arguments is [var l] && l.Equals("max", StringComparison.OrdinalIgnoreCase) ? -1 : Argument(0, 1)),
            "decimal" or "numeric" => new ScalarType(name, Precision: Argument(0, 18), Scale: Argument(1, 0)),
            "datetime2" or "time" or "datetimeoffset" => new ScalarType(name, Precision: Argument(0, 7)),
            "money" => new ScalarType(name, Precision: 19, Scale: 4),
            "smallmoney" => new ScalarType(name, Precision: 10, Scale: 4),
            "tinyint" or "smallint" or "int" or "bigint" or "bit" or "float" or "real" or "date" or "datetime" or "smalldatetime" or "uniqueidentifier" => new ScalarType(name),
            _ => null,
        };
    }

    /// <summary>A string type of this length, capped as concatenation caps it, or max.</summary>
    public static ScalarType String(bool national, bool varying, int length) => new(
        (national ? "n" : "") + (varying ? "varchar" : "char"),
        length < 0 ? -1 : Math.Min(length, national ? MostBytes / 2 : MostBytes));

    // ---- exact numbers -------------------------------------------------------------------------------------------------

    private static int Rank(string integer) => integer switch
    {
        "bit" => 0,
        "tinyint" => 1,
        "smallint" => 2,
        "int" => 3,
        "bigint" => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(integer), integer, "Not an integer type."),
    };

    /// <summary>The integer digits a type's values can have: <c>int</c> 10, <c>smallint</c> 5.</summary>
    private static int IntegerDigits(string integer) => integer switch
    {
        "bit" => 1,
        "tinyint" => 3,
        "smallint" => 5,
        "int" => 10,
        "bigint" => 19,
        _ => throw new ArgumentOutOfRangeException(nameof(integer), integer, "Not an integer type."),
    };

    /// <summary>
    /// The decimal an exact number is converted to beside a decimal: its own precision and scale; an integer column's
    /// digits, or an integer literal's own (observed); money as decimal(19, 4).
    /// </summary>
    private (int Precision, int Scale) AsDecimal() => this switch
    {
        { IsDecimal: true } or { Name: "money" or "smallmoney" } => (Precision, Scale),
        { Digits: { } digits } => (digits, 0),
        _ => (IntegerDigits(Name), 0),
    };

    /// <summary>Whether it is named numeric rather than decimal: a numeric column or a decimal literal.</summary>
    private bool IsNumericNamed => Name == "numeric";

    // ---- COALESCE --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The type <c>COALESCE</c> gives, the one of highest precedence wide enough for every argument, or null when it is not
    /// modelled (observed: <c>Coalesce_takes_the_type_of_highest_precedence_and_isnull_the_first_arguments</c>):
    /// <list type="bullet">
    /// <item>strings: nvarchar if any is national, varchar if any varies, of the longest length, max if any is;</item>
    /// <item>exact numbers with a decimal: a decimal holding every argument's integer digits and the largest scale, named as its
    /// first decimal argument; without one, the widest integer;</item>
    /// <item>a date with datetime2 of one precision: that datetime2; otherwise one type, the same for all.</item>
    /// </list>
    /// Every argument then converts to it without an overflow.
    /// </summary>
    public static ScalarType? Union(IReadOnlyList<ScalarType> types)
    {
        if (types.Count == 0)
            return null;
        if (types.All(t => t.IsString))
        {
            var varying = types.Any(t => t.Name.EndsWith("varchar", StringComparison.Ordinal));
            return String(types.Any(t => t.IsNational), varying, types.Any(t => t.IsMax) ? -1 : types.Max(t => t.Length));
        }

        if (types.All(t => t.IsInteger || t.IsDecimal) && types.FirstOrDefault(t => t.IsDecimal) is { } first)
        {
            var decimals = types.Select(t => t.AsDecimal()).ToList();
            var scale = decimals.Max(d => d.Scale);
            var precision = decimals.Max(d => d.Precision - d.Scale) + scale;
            return precision > 38 ? null : new ScalarType(first.Name, Precision: precision, Scale: scale);
        }

        if (types.All(t => t.IsInteger))
            return types.MaxBy(t => Rank(t.Name))! with { Digits = null };
        if (types.All(t => t.Name is "date" or "datetime2") && types.Where(t => t.Name == "datetime2").Select(t => t.Precision).Distinct().Count() == 1)
            return types.First(t => t.Name == "datetime2");
        return types.Select(t => t.Text).Distinct().Count() == 1 ? types[0] with { Digits = null } : null;
    }

    // ---- arithmetic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The type of <c>l op r</c> for exact numbers, or null when it is not modelled (observed:
    /// <c>Decimal_arithmetic_has_the_documented_precision_and_scale</c>). Two integers give the wider. With a decimal: + and -
    /// give scale max(s1, s2), precision max(p1 - s1, p2 - s2) + scale + 1; * gives p1 + p2 + 1, s1 + s2; / gives scale
    /// max(6, s1 + p2 + 1), precision p1 - s1 + s2 + scale; numeric when either is. Beyond 38 digits SQL Server cuts the scale,
    /// which is not modelled. Money, float and dates are not modelled.
    /// </summary>
    public static ScalarType? Arithmetic(char op, ScalarType left, ScalarType right)
    {
        if (left.IsInteger && right.IsInteger)
            return (Rank(left.Name) >= Rank(right.Name) ? left : right) with { Digits = null };
        if (!(left.IsInteger || left.IsDecimal) || !(right.IsInteger || right.IsDecimal))
            return null;

        var (p1, s1) = left.AsDecimal();
        var (p2, s2) = right.AsDecimal();
        int precision, scale;
        switch (op)
        {
            case '+' or '-':
                scale = Math.Max(s1, s2);
                precision = Math.Max(p1 - s1, p2 - s2) + scale + 1;
                break;
            case '*':
                precision = p1 + p2 + 1;
                scale = s1 + s2;
                break;
            case '/':
                scale = Math.Max(6, s1 + p2 + 1);
                precision = p1 - s1 + s2 + scale;
                break;
            default:
                return null;
        }

        var name = left.IsNumericNamed || right.IsNumericNamed ? "numeric" : "decimal";
        return precision > 38 ? null : new ScalarType(name, Precision: precision, Scale: scale);
    }

    // ---- conversion -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether every value of <paramref name="from"/> converts to <paramref name="to"/> without an overflow, which is an error by
    /// default and NULL under a caller's ANSI_WARNINGS and ARITHABORT off (observed:
    /// <c>An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off</c>). A conversion that
    /// fails for a value (a string that is not a number) is an error even then
    /// (<c>A_failed_conversion_is_an_error_even_under_ansi_warnings_and_arithabort_off</c>), so only overflow makes NULL. False
    /// when not proved: each case below was observed at its edge (<c>A_conversion_that_cannot_overflow_keeps_every_value</c>).
    /// </summary>
    /// <param name="styled">Whether CONVERT gives a style, which changes a number's characters, so a number's length is not known.</param>
    public static bool Converts(ScalarType from, ScalarType to, bool styled = false)
    {
        if (from.Text == to.Text && !styled)
            return true;

        if (to.IsString)
        {
            // A string, a date or a time is cut short to fit, as strings are; anything else must fit whole.
            if (from.IsString || from.IsDateOrTime)
                return true;
            if (styled)
                return false;
            var longest = from switch
            {
                { Name: "bit" } => 1,
                { Name: "tinyint" } => 3,
                { Name: "smallint" } => 6,
                { Name: "int" } => 11,
                { Name: "bigint" } => 20,
                { Name: "uniqueidentifier" } => 36,

                // A sign, the digits, a point when there are places, and a 0 before it when there are only places: -0.5.
                { IsDecimal: true } => from.Precision + 1 + (from.Scale > 0 ? 1 : 0) + (from.Precision == from.Scale ? 1 : 0),
                _ => int.MaxValue,
            };
            return to.IsMax ? longest < int.MaxValue : to.Length >= longest;
        }

        if (to.IsInteger)
        {
            if (from.IsInteger || from.Name == "bit")
                return Rank(to.Name) >= Rank(from.Name);

            // A decimal is cut to its integer part, which must fit with any sign: tinyint has none.
            return from.IsDecimal && to.Name != "tinyint" && from.Precision - from.Scale <= IntegerDigits(to.Name) - 1;
        }

        if (to.IsDecimal)
        {
            if (!from.IsExact)
                return false;

            // Rounding to fewer places can carry into one more integer digit: 99.999 as decimal(4,2) is 100.00.
            var (p, s) = from.AsDecimal();
            return to.Precision - to.Scale > p - s || (to.Precision - to.Scale == p - s && to.Scale >= s);
        }

        return to.Name switch
        {
            "bit" => from.IsExact,
            "float" => from.IsExact || from.Name is "real" or "float",
            "real" => from.IsExact || from.Name == "real",
            "money" => (from.IsInteger && from.Name != "bigint") || from.Name is "bit" or "smallmoney" || (from.IsDecimal && from.Precision - from.Scale <= 14),
            "date" => from.Name is "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset",
            "datetime2" => from.Name switch
            {
                "date" or "smalldatetime" => true,
                "datetime" => to.Precision >= 3,
                "datetime2" => to.Precision >= from.Precision,
                _ => false,
            },
            "datetime" => from.Name is "datetime" or "smalldatetime",
            "time" => from.Name == "time" && to.Precision >= from.Precision,
            _ => false,
        };
    }

    /// <summary>
    /// Whether a literal's value converts to <paramref name="to"/> without an overflow: an integer within its range, or within
    /// a decimal's integer digits; a decimal literal rounded to the target's places; a string into a string. Null when not
    /// decided here, so the type's rule applies.
    /// </summary>
    public static bool? Fits(Microsoft.SqlServer.TransactSql.ScriptDom.Literal literal, ScalarType to, bool negative)
    {
        if (literal is Microsoft.SqlServer.TransactSql.ScriptDom.StringLiteral)
            return to.IsString ? true : null;
        if (literal is not (Microsoft.SqlServer.TransactSql.ScriptDom.IntegerLiteral or Microsoft.SqlServer.TransactSql.ScriptDom.NumericLiteral)
            || !decimal.TryParse(literal.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return null;
        if (negative)
            value = -value;

        if (to.IsInteger)
        {
            var whole = decimal.Truncate(value);
            return to.Name switch
            {
                "tinyint" => whole is >= byte.MinValue and <= byte.MaxValue,
                "smallint" => whole is >= short.MinValue and <= short.MaxValue,
                "int" => whole is >= int.MinValue and <= int.MaxValue,
                _ => whole is >= long.MinValue and <= long.MaxValue,
            };
        }

        if (to.IsDecimal)
        {
            var rounded = decimal.Round(value, Math.Min(to.Scale, 28), MidpointRounding.AwayFromZero);
            var integer = decimal.Truncate(decimal.Abs(rounded));
            var digits = integer == 0 ? 0 : integer.ToString(CultureInfo.InvariantCulture).Length;
            return digits <= to.Precision - to.Scale;
        }

        return null;
    }
}
