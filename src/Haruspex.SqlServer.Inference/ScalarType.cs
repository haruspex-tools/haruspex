using System.Globalization;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A built-in scalar type as SQL Server types an expression: what <c>COALESCE</c>, conversions, string building and
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

    /// <summary>money or smallmoney.</summary>
    public bool IsMoney => Name is "money" or "smallmoney";

    /// <summary>float or real.</summary>
    public bool IsApproximate => Name is "float" or "real";

    /// <summary>A number data type precedence ranks: an integer, decimal, money or approximate number (not bit).</summary>
    public bool IsNumber => IsInteger || IsDecimal || IsMoney || IsApproximate;

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

    // ---- combining types ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Data type precedence, highest first, for the types modelled here (Microsoft's "Data type precedence"): what a column
    /// of a set operation, <c>COALESCE</c>, <c>CASE</c>, <c>IIF</c> and <c>CHOOSE</c> take when their values' types differ.
    /// </summary>
    private static int PrecedenceRank(string name) => name switch
    {
        "datetimeoffset" => 19,
        "datetime2" => 18,
        "datetime" => 17,
        "smalldatetime" => 16,
        "date" => 15,
        "time" => 14,
        "float" => 13,
        "real" => 12,
        "decimal" or "numeric" => 11,
        "money" => 10,
        "smallmoney" => 9,
        "bigint" => 8,
        "int" => 7,
        "smallint" => 6,
        "tinyint" => 5,
        "bit" => 4,
        "uniqueidentifier" => 3,
        "nvarchar" => 2,
        "nchar" => 1,
        "varchar" => 0,
        "char" => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a type modelled here."),
    };

    /// <summary>
    /// The server's type rule for a chain: the type a column takes whose values are of these types, as a set operation,
    /// <c>COALESCE</c>, <c>CASE</c>, <c>IIF</c> and <c>CHOOSE</c> all type it (one rule; observed over every ordered pair of a grid, SetTypeGridTests, and in
    /// SetTypeObservationTests), or null when SQL Server refuses them (<see cref="Clash"/>). It is a fold, left to right, of
    /// <see cref="Pair"/> (observed: A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal): bigint, then
    /// money, then decimal(5,2) is decimal(19,4), money having taken the bigint in; bigint, decimal(5,2), money is
    /// decimal(23,4). The date and time types are the exception: their fractional digits are the most over every value
    /// (observed: A_chain_takes_the_most_fractional_digits_of_every_date_and_time). Whether each value converts to the result
    /// without an overflow is the caller's question (<see cref="Converts"/>).
    /// <para>
    /// It is not a join. A join is associative and commutative, and the fold is neither: money between bigint and decimal(5,2)
    /// changes the result. So it is never used to say what is known where paths meet (<see cref="Value.Join"/>), and nothing
    /// regroups or reorders a chain's types before folding them.
    /// </para>
    /// </summary>
    public static ScalarType? Chain(IReadOnlyList<ScalarType> types) => Clash(types) is null ? Fold(types) : null;

    private static ScalarType? Fold(IReadOnlyList<ScalarType> types)
    {
        if (types.Count == 0)
            return null;
        static int Fraction(ScalarType t) => t.Name switch
        {
            "datetimeoffset" or "datetime2" or "time" => t.Precision,
            "datetime" => 3,
            _ => 0,
        };

        var folded = types.Skip(1).Aggregate(types[0], Pair);
        return folded.Name switch
        {
            "datetimeoffset" or "datetime2" => folded with { Precision = types.Max(Fraction) },
            "time" => folded with { Precision = types.Where(t => t.Name == "time").Max(t => t.Precision) },
            _ => folded with { Digits = null },
        };
    }

    /// <summary>
    /// Two types combined, as SQL Server does at each step of a chain: the one of higher precedence (<see cref="PrecedenceRank"/>),
    /// whatever the range, with
    /// <list type="bullet">
    /// <item>two integers the wider, counting the more digits (an integer literal its own);</item>
    /// <item>an integer and money or smallmoney that money type, its digits only (15 or 6): the integer's are gone;</item>
    /// <item>a decimal and an exact number a decimal holding both's integer digits and the larger scale, past 38 digits the
    /// scale cut to 38 less those digits with no floor, named as the first decimal; bit beside it adds nothing;</item>
    /// <item>two strings national if either is, varying if either is, the longer (cut at 4000 for national), max if either is.</item>
    /// </list>
    /// A pair SQL Server would refuse still gives the higher one: a chain is refused only when a value cannot become the
    /// final type (<see cref="Clash"/>; observed: A_chain_is_refused_only_when_a_value_cannot_become_its_type).
    /// </summary>
    private static ScalarType Pair(ScalarType a, ScalarType b)
    {
        static bool Exact(ScalarType t) => t.IsInteger || t.IsDecimal || t.IsMoney;
        if ((a.IsDecimal || b.IsDecimal) && Exact(a) && Exact(b))
        {
            var (pa, sa) = a.AsDecimal();
            var (pb, sb) = b.AsDecimal();
            var integer = Math.Max(pa - sa, pb - sb);
            var scale = Math.Min(Math.Max(sa, sb), 38 - integer);
            return new ScalarType(a.IsDecimal ? a.Name : b.Name, Precision: integer + scale, Scale: scale);
        }

        if (a.IsInteger && b.IsInteger)
        {
            var wider = PrecedenceRank(a.Name) >= PrecedenceRank(b.Name) ? a : b;
            var digits = Math.Max(a.AsDecimal().Precision, b.AsDecimal().Precision);
            return wider with { Digits = digits == IntegerDigits(wider.Name) ? null : digits };
        }

        if (a.IsString && b.IsString)
        {
            var varying = a.Name.EndsWith("varchar", StringComparison.Ordinal) || b.Name.EndsWith("varchar", StringComparison.Ordinal);
            return String(a.IsNational || b.IsNational, varying, a.IsMax || b.IsMax ? -1 : Math.Max(a.Length, b.Length));
        }

        // bit beside an integer adds nothing: an integer literal keeps its digits (observed over the grid's triples).
        var top = PrecedenceRank(a.Name) >= PrecedenceRank(b.Name) ? a : b;
        return top.IsDecimal || top.IsInteger ? top : top with { Digits = null };
    }

    /// <summary>
    /// A value SQL Server refuses to convert to the type the chain folds to, and a value of that type (or the type itself), in
    /// the order they come; null when every value converts (observed:
    /// Types_without_an_implicit_conversion_are_refused_with_an_operand_type_clash, and every pair in SetTypeGridTests; for a
    /// chain, A_chain_is_refused_only_when_a_value_cannot_become_its_type: 1, a date, a datetime is datetime, though 1 and a
    /// date alone are refused). Refused: uniqueidentifier with anything but a string; date, time, datetime2 and datetimeoffset
    /// with a number or bit; date with time.
    /// </summary>
    public static (ScalarType First, ScalarType Second)? Clash(IReadOnlyList<ScalarType> types)
    {
        static bool Numeric(ScalarType t) => t.IsNumber || t.Name == "bit";
        static bool Refused(ScalarType a, ScalarType b) =>
            (a.Name == "uniqueidentifier" && b.Name != "uniqueidentifier" && !b.IsString)
            || (a.Name is "date" or "time" or "datetime2" or "datetimeoffset" && Numeric(b))
            || (a.Name == "date" && b.Name == "time");

        if (Fold(types) is not { } final)
            return null;
        var own = types.ToList().FindIndex(t => t.Name == final.Name);
        for (var i = 0; i < types.Count; i++)
        {
            if (!Refused(types[i], final) && !Refused(final, types[i]))
                continue;
            if (own < 0)
                return (types[i], final);
            return own < i ? (types[own], types[i]) : (types[i], types[own]);
        }

        return null;
    }

    /// <summary>
    /// Whether every value of <paramref name="from"/> is the same value in <paramref name="to"/>, so the facts proved of it
    /// (bounds, lengths, a closed set) still hold once converted: an exact number into one holding all its integer digits and
    /// places, a string into a varying string as long, of the same kind. Not a float (whose digits are approximate), not a
    /// rounded decimal, not a string padded or cut short.
    /// </summary>
    public static bool Preserves(ScalarType from, ScalarType to)
    {
        if (from.Text == to.Text)
            return true;
        if (from.IsString && to.IsString)
            return from.IsNational == to.IsNational && to.Name.EndsWith("varchar", StringComparison.Ordinal) && (to.IsMax || (!from.IsMax && to.Length >= from.Length));
        if (!(from.IsInteger || from.IsDecimal || from.IsMoney) || !Converts(from, to))
            return false;
        if (to.IsInteger || to.IsMoney)
            return from.IsInteger || (from.Name == "smallmoney" && to.Name == "money");
        if (!to.IsDecimal)
            return false;
        var (p, s) = from.AsDecimal();
        return to.Precision - to.Scale >= p - s && to.Scale >= s;
    }

    // ---- arithmetic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The type of <c>l op r</c>, or null when it is not modelled (observed:
    /// <c>Decimal_arithmetic_has_the_documented_precision_and_scale</c>,
    /// <c>Arithmetic_over_float_real_money_and_dates_follows_data_type_precedence</c>,
    /// <c>Decimal_arithmetic_beyond_38_digits_caps_the_precision_and_cuts_the_scale</c>,
    /// <c>Modulo_has_the_documented_precision_and_scale_and_integers_and_money_keep_their_type</c>,
    /// <c>A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error</c>; and over a grid, ArithmeticGridTests):
    /// <list type="bullet">
    /// <item>datetime or smalldatetime + or - a number, or each other: datetime if either is, else smalldatetime (datetime2,
    /// date, time and datetimeoffset take no arithmetic: an error);</item>
    /// <item>a bit beside another number is converted to that number's own type, a decimal's precision and scale too; two bits
    /// are an error;</item>
    /// <item>a float with any number: float; a real: real; neither takes <c>%</c> (an error);</item>
    /// <item>money or smallmoney with an integer or each other: money if either is, else smallmoney (even beside bigint);</item>
    /// <item>two integers: the wider;</item>
    /// <item>with a decimal, by <see cref="DecimalResult"/>; numeric when either is.</item>
    /// </list>
    /// </summary>
    public static ScalarType? Arithmetic(char op, ScalarType left, ScalarType right)
    {
        if (left.Name is "datetime" or "smalldatetime" || right.Name is "datetime" or "smalldatetime")
        {
            static bool Operand(ScalarType t) => t.Name is "datetime" or "smalldatetime" or "bit" || t.IsNumber;
            return op is '+' or '-' && Operand(left) && Operand(right)
                ? new ScalarType(left.Name == "datetime" || right.Name == "datetime" ? "datetime" : "smalldatetime")
                : null;
        }

        if (Operands(left, right) is not var (l, r))
            return null;
        if (l.IsApproximate || r.IsApproximate)
            return op == '%' ? null : new ScalarType(l.Name == "float" || r.Name == "float" ? "float" : "real");
        if ((l.IsMoney || r.IsMoney) && !l.IsDecimal && !r.IsDecimal)
            return Parse(l.Name == "money" || r.Name == "money" ? "money" : "smallmoney");
        if (l.IsInteger && r.IsInteger)
            return (Rank(l.Name) >= Rank(r.Name) ? l : r) with { Digits = null };

        var name = l.IsNumericNamed || r.IsNumericNamed ? "numeric" : "decimal";
        return DecimalResult(op, l, r) is var (precision, scale, _, _) ? new ScalarType(name, Precision: precision, Scale: scale) : null;
    }

    /// <summary>
    /// The operands as SQL Server computes with them: a bit beside another number becomes that number's type (observed:
    /// <c>A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error</c>, bit * decimal(10,2) is decimal(21,4)); null
    /// unless both are numbers, and for two bits.
    /// </summary>
    private static (ScalarType Left, ScalarType Right)? Operands(ScalarType left, ScalarType right)
    {
        static bool Numeric(ScalarType t) => t.IsNumber || t.Name == "bit";
        if (!Numeric(left) || !Numeric(right) || (left.Name == "bit" && right.Name == "bit"))
            return null;
        return (left.Name == "bit" ? right : left, right.Name == "bit" ? left : right);
    }

    /// <summary>
    /// The precision and scale of decimal arithmetic, money as decimal(19,4), smallmoney decimal(10,4), an integer as its digits,
    /// as Microsoft documents it ("Precision, scale, and length", linked from README.md), and the precision and scale before any cap:
    /// <list type="bullet">
    /// <item>+ and - give scale max(s1, s2), precision max(p1 - s1, p2 - s2) + scale + 1;</item>
    /// <item>* gives p1 + p2 + 1, s1 + s2;</item>
    /// <item>/ gives scale max(6, s1 + p2 + 1), precision p1 - s1 + s2 + scale;</item>
    /// <item>% gives scale max(s1, s2), precision min(p1 - s1, p2 - s2) + scale, never past 38.</item>
    /// </list>
    /// Past 38 digits the precision is 38 and the scale is cut to keep the integer digits: for + and -, to
    /// 38 - max(p1 - s1, p2 - s2), with no floor; for * and /, to min(s, max(6, 38 - (p - s))), so never below 6 unless it was
    /// already (observed over 144,500 operand pairs when the rule was written; ArithmeticGridTests keeps a grid of them).
    /// </summary>
    private static (int Precision, int Scale, int UncappedPrecision, int UncappedScale)? DecimalResult(char op, ScalarType left, ScalarType right)
    {
        if (!(left.IsInteger || left.IsDecimal || left.IsMoney) || !(right.IsInteger || right.IsDecimal || right.IsMoney))
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
            case '%':
                scale = Math.Max(s1, s2);
                precision = Math.Min(p1 - s1, p2 - s2) + scale;
                break;
            default:
                return null;
        }

        if (precision <= 38)
            return (precision, scale, precision, scale);
        var cut = op is '+' or '-'
            ? Math.Min(scale, 38 - Math.Max(p1 - s1, p2 - s2))
            : Math.Min(scale, Math.Max(6, 38 - (precision - scale)));
        return (38, cut, precision, scale);
    }

    /// <summary>
    /// Whether <c>l op r</c> may overflow its type or divide by zero, which is an error by default and NULL under a caller's
    /// ANSI_WARNINGS and ARITHABORT off (observed:
    /// <c>Decimal_overflow_and_division_by_zero_are_errors_by_default_and_null_under_the_callers_options</c>,
    /// <c>Integer_and_money_division_overflow_only_at_the_least_value_divided_by_minus_one</c>). True unless proved:
    /// <list type="bullet">
    /// <item>integers, money and floats can always overflow, but a division or modulo by a literal of at least 1 neither
    /// divides by zero nor grows (the least int divided by -1 is why the literal must be positive);</item>
    /// <item>decimals cannot overflow within 38 digits, except that a bit beside decimal(p, p) becomes that type, which cannot
    /// hold 1; a division or modulo divides by zero unless the divisor is a literal other than 0. <c>%</c> brings both
    /// operands to its scale max(s1, s2) first, which overflows when max(p1 - s1, p2 - s2) + max(s1, s2) passes 38; its result,
    /// smaller than either operand, cannot. Capped past 38 digits, the result
    /// overflows when the integer digits it may need exceed the 38 - scale it keeps: max(p1 - s1, p2 - s2) + 1 for + and -,
    /// (p1 - s1) + (p2 - s2) for *, p1 - s1 + s2 for / (p1 - s1 by a literal of at least 1). For * and / it may also when they
    /// are equal and the scale was cut, since rounding can carry into one more digit (observed: decimal(38,38) squared rounds
    /// up to 1).</item>
    /// </list>
    /// </summary>
    /// <param name="divisor">The right operand's value, when it is a number literal.</param>
    public static bool MayOverflow(char op, ScalarType left, ScalarType right, decimal? divisor)
    {
        if (Operands(left, right) is not var (l, r))
            return true;
        var byPositive = op is '/' or '%' && divisor >= 1;
        if (l.IsApproximate || r.IsApproximate || (!l.IsDecimal && !r.IsDecimal))
            return !byPositive;
        if (op is '/' or '%' && divisor is null or 0)
            return true;

        // A bit becomes the decimal's own type, which cannot hold its 1 when every digit is a place (observed:
        // A_bit_converted_to_a_decimal_of_places_only_overflows_when_it_is_1).
        if ((left.Name == "bit" || right.Name == "bit") && l.IsDecimal && l.Precision == l.Scale)
            return true;

        var (p1, s1) = l.AsDecimal();
        var (p2, s2) = r.AsDecimal();

        // % first brings each operand to its scale, keeping its integer digits, which past 38 digits overflows (observed:
        // Modulo_brings_both_operands_to_its_scale_first_which_can_overflow); its result, smaller than either, cannot.
        if (op == '%')
            return Math.Max(p1 - s1, p2 - s2) + Math.Max(s1, s2) > 38;

        // Within 38 digits the precision holds every result, so only a capped one can overflow.
        if (DecimalResult(op, l, r) is not var (_, scale, uncappedPrecision, uncappedScale) || uncappedPrecision <= 38)
            return false;

        var needed = op switch
        {
            '+' or '-' => Math.Max(p1 - s1, p2 - s2) + 1,
            '*' => p1 - s1 + (p2 - s2),
            _ => byPositive ? p1 - s1 : p1 - s1 + s2,
        };
        var kept = 38 - scale;
        return needed > kept || (needed == kept && op is '*' or '/' && scale < uncappedScale);
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
            "smallmoney" => from.Name is "tinyint" or "smallint" or "bit" or "smallmoney" || (from.IsDecimal && from.Precision - from.Scale <= 5),
            "float" => from.IsExact || from.Name is "real" or "float",
            "real" => from.IsExact || from.Name == "real",
            "money" => (from.IsInteger && from.Name != "bigint") || from.Name is "bit" or "smallmoney" || (from.IsDecimal && from.Precision - from.Scale <= 14),
            "date" => from.Name is "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset",

            // A time is that time on 1 January 1900, rounding into the 2nd at most; a tinyint or bit that many days from 1900,
            // a smallint too within datetime's range (1753 to 9999) but not smalldatetime's (1900 to 2079); a date before
            // 1753 or 1900 overflows (observed: A_conversion_that_cannot_overflow_keeps_every_value_at_its_edges,
            // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options).
            "smalldatetime" => from.Name is "smalldatetime" or "time" or "tinyint" or "bit",
            "datetime2" => from.Name switch
            {
                "date" or "smalldatetime" => true,
                "datetime" => to.Precision >= 3,
                "datetime2" or "time" => to.Precision >= from.Precision,
                _ => false,
            },
            "datetimeoffset" => from.Name switch
            {
                "date" or "smalldatetime" => true,
                "datetime" => to.Precision >= 3,
                "datetime2" or "time" or "datetimeoffset" => to.Precision >= from.Precision,
                _ => false,
            },
            "datetime" => from.Name is "datetime" or "smalldatetime" or "time" or "tinyint" or "smallint" or "bit",
            "time" => from.Name == "time" && to.Precision >= from.Precision,
            _ => false,
        };
    }

    /// <summary>
    /// Whether converting a value of <paramref name="from"/> to <paramref name="to"/> can never give NULL for a value that is
    /// not: <see cref="Converts"/>, or a string into uniqueidentifier, which is the text's first 36 characters or fails, an error
    /// under any options (observed: A_string_that_is_not_a_value_of_the_type_is_an_error_even_under_the_callers_options). Not
    /// in <see cref="Converts"/>, which also says TRY_CAST cannot fail.
    /// </summary>
    public static bool NeverNull(ScalarType from, ScalarType to) => Converts(from, to) || (from.IsString && to.Name == "uniqueidentifier");

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
            || !decimal.TryParse(literal.Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

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
