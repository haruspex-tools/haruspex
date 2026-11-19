using System.Globalization;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A built-in type with its arguments, the defaults applied: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>, <c>datetime2(7)</c>. What
/// a column's facts are read from in Semantics, and what the server's type rules compute from and give in Inference.
/// </summary>
/// <param name="Type">The built-in type.</param>
/// <param name="Length">A string's length (bytes for char and varchar, characters for nchar and nvarchar); -1 for max.</param>
/// <param name="Precision">A decimal's precision, money's fixed one, or a date or time type's fractional digits.</param>
/// <param name="Scale">A decimal's scale, or money's fixed one.</param>
/// <remarks>
/// A type has no integer digits of its own beyond its type's (<see cref="BuiltInType.Digits"/>): the digits an integer
/// literal counts are an operand's, and travel beside the type in Inference.
/// </remarks>
public sealed record ScalarType(BuiltInType Type, int Length = 0, int Precision = 0, int Scale = 0)
{
    /// <summary>The type, of no arguments or of money's fixed digits: <c>int</c>, <c>money</c>.</summary>
    public static ScalarType Of(BuiltInType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Family == TypeFamily.Money && type.Digits is var (precision, scale)
            ? new ScalarType(type, Precision: precision, Scale: scale)
            : new ScalarType(type);
    }

    /// <summary>
    /// A built-in type as declared, with the defaults of a declaration: <c>decimal</c> is <c>decimal(18,0)</c>, <c>char</c>
    /// <c>char(1)</c>, and <c>float(24)</c> is <c>real</c>. Null for a type that isn't built in, or whose arguments it doesn't
    /// take: not whole numbers in range (or <c>max</c>, for a varying type), or any for a type that takes none.
    /// </summary>
    public static ScalarType? Of(SqlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return BuiltInType.Of(type) is { } builtIn ? Of(builtIn, type.Arguments) : null;
    }

    /// <summary>
    /// A built-in type as the IR writes it, <c>decimal(19,2)</c>, read as <see cref="Of(SqlType)"/> reads a declaration. Null
    /// for anything else.
    /// </summary>
    public static ScalarType? Parse(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
            return null;
        var open = sql.IndexOf('(', StringComparison.Ordinal);
        var name = open < 0 ? sql : sql[..open];
        var arguments = open < 0 || !sql.EndsWith(')') ? [] : sql[(open + 1)..^1].Split(',');
        return BuiltInType.Named(name) is { } type ? Of(type, arguments) : null;
    }

    private static ScalarType? Of(BuiltInType type, IReadOnlyList<string> arguments)
    {
        int? Whole(int i, int absent) =>
            arguments.Count <= i ? absent
            : int.TryParse(arguments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n
            : null;

        switch (type.Arguments)
        {
            case TypeArgumentKind.Length when arguments is [var only] && only.Equals("max", StringComparison.OrdinalIgnoreCase):
                return type.IsVarying ? new ScalarType(type, -1) : null;
            case TypeArgumentKind.Length:
                return arguments.Count <= 1 && Whole(0, BuiltInType.LengthDeclared) is { } length ? new ScalarType(type, length) : null;
            case TypeArgumentKind.PrecisionAndScale:
                var (p, s) = BuiltInType.DigitsDeclared;
                return arguments.Count <= 2 && Whole(0, p) is { } precision && Whole(1, s) is { } scale
                    ? new ScalarType(type, Precision: precision, Scale: scale)
                    : null;
            case TypeArgumentKind.FractionalSeconds:
                return arguments.Count <= 1 && Whole(0, BuiltInType.FractionalSecondsDeclared) is { } fraction
                    ? new ScalarType(type, Precision: fraction)
                    : null;
            case TypeArgumentKind.Dimensions:
                return arguments is [_] && Whole(0, 0) is { } dimensions ? new ScalarType(type, Precision: dimensions) : null;
            case TypeArgumentKind.MantissaBits:
                return arguments.Count > 1 || Whole(0, BuiltInType.FloatMantissaBits) is not { } bits || bits is < 1 || bits > BuiltInType.FloatMantissaBits ? null
                    : Of(bits <= BuiltInType.RealMantissaBits ? BuiltInType.Real : type);
            default:
                return arguments.Count == 0 ? Of(type) : null;
        }
    }

    /// <summary>A string type of this length, capped as concatenation caps it, or max.</summary>
    public static ScalarType CharacterString(bool national, bool varying, int length)
    {
        var type = (national, varying) switch
        {
            (true, true) => BuiltInType.NVarChar,
            (true, false) => BuiltInType.NChar,
            (false, true) => BuiltInType.VarChar,
            _ => BuiltInType.Char,
        };
        return new ScalarType(type, length < 0 ? -1 : Math.Min(length, type.MostLength));
    }

    /// <summary>
    /// A binary type of this length, capped at 8000 bytes as concatenation caps it, or max: <c>binary(n)</c> when fixed,
    /// <c>varbinary(n)</c> when varying.
    /// </summary>
    public static ScalarType ByteString(bool varying, int length) =>
        new(varying || length < 0 ? BuiltInType.VarBinary : BuiltInType.Binary, length < 0 ? -1 : Math.Min(length, BuiltInType.MostBytes));

    /// <summary>The type's family.</summary>
    public TypeFamily Family => Type.Family;

    /// <summary>A binary string: binary or varbinary (not rowversion, nor image, which have rules of their own).</summary>
    public bool IsBinary => Family == TypeFamily.Binary;

    /// <summary>A string: char, varchar, nchar or nvarchar.</summary>
    public bool IsString => Family == TypeFamily.String;

    /// <summary>Whether its text is Unicode.</summary>
    public bool IsNational => Type.IsNational;

    /// <summary>Whether its length varies.</summary>
    public bool IsVarying => Type.IsVarying;

    /// <summary>
    /// Whether a nullable column of it, created while <c>ANSI_PADDING</c> is off, keeps its values with their padding cut:
    /// <c>char(n)</c> its trailing spaces, in every collation, and <c>binary(n)</c> its trailing zeros. <c>nchar(n)</c> pads
    /// whatever the setting (observed: A_nullable_char_column_created_with_ansi_padding_off_cuts_trailing_spaces_but_nchar_and_not_null_pad,
    /// A_nullable_binary_column_created_with_ansi_padding_off_cuts_trailing_zeros_and_a_not_null_one_pads).
    /// </summary>
    public bool IsPaddingCutWithoutAnsiPadding => !IsVarying && !IsMax && (IsBinary || (IsString && !IsNational));

    /// <summary>Whether it is of max length.</summary>
    public bool IsMax => Length < 0;

    /// <summary>bit.</summary>
    public bool IsBit => Family == TypeFamily.Bit;

    /// <summary>tinyint, smallint, int or bigint.</summary>
    public bool IsInteger => Family == TypeFamily.Integer;

    /// <summary>decimal or numeric.</summary>
    public bool IsDecimal => Family == TypeFamily.Decimal;

    /// <summary>A number with an exact decimal form: an integer, bit, decimal, numeric, money or smallmoney.</summary>
    public bool IsExact => Family is TypeFamily.Integer or TypeFamily.Decimal or TypeFamily.Bit or TypeFamily.Money;

    /// <summary>A date or time type.</summary>
    public bool IsDateOrTime => Family == TypeFamily.DateAndTime;

    /// <summary>money or smallmoney.</summary>
    public bool IsMoney => Family == TypeFamily.Money;

    /// <summary>float or real.</summary>
    public bool IsApproximate => Family == TypeFamily.Approximate;

    /// <summary>A number data type precedence ranks: an integer, decimal, money or approximate number (not bit).</summary>
    public bool IsNumber => Family is TypeFamily.Integer or TypeFamily.Decimal or TypeFamily.Money or TypeFamily.Approximate;

    /// <summary>The integer digits of a type of fixed digits: <c>int</c> 10, <c>bit</c> 1.</summary>
    public int IntegerDigits => Type.Digits is var (precision, scale) ? precision - scale
        : throw new InvalidOperationException($"{Type.Name} has no fixed digits.");

    /// <summary>
    /// The fractional digits of a date or time: its argument's, or its type's own (<c>datetime</c> 3); 0 for any other type.
    /// </summary>
    public int FractionalSeconds => Type.Arguments == TypeArgumentKind.FractionalSeconds ? Precision : Type.FractionalSeconds ?? 0;

    /// <summary>The type as the IR writes it: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>.</summary>
    public string Text => Type.Arguments switch
    {
        TypeArgumentKind.Length => $"{Type.Name}({(IsMax ? "max" : Length.ToString(CultureInfo.InvariantCulture))})",
        TypeArgumentKind.PrecisionAndScale => $"{Type.Name}({Precision.ToString(CultureInfo.InvariantCulture)},{Scale.ToString(CultureInfo.InvariantCulture)})",
        TypeArgumentKind.FractionalSeconds or TypeArgumentKind.Dimensions => $"{Type.Name}({Precision.ToString(CultureInfo.InvariantCulture)})",
        _ => Type.Name,
    };

    /// <summary>The type as a declaration would write it, which is how the model holds a type.</summary>
    public SqlType Declaration => Type.Arguments switch
    {
        TypeArgumentKind.Length => new SqlType(SqlTypeKind.BuiltIn, Type.Name, [IsMax ? "max" : Length.ToString(CultureInfo.InvariantCulture)]),
        TypeArgumentKind.PrecisionAndScale => new SqlType(SqlTypeKind.BuiltIn, Type.Name, [Precision.ToString(CultureInfo.InvariantCulture), Scale.ToString(CultureInfo.InvariantCulture)]),
        TypeArgumentKind.FractionalSeconds or TypeArgumentKind.Dimensions => new SqlType(SqlTypeKind.BuiltIn, Type.Name, [Precision.ToString(CultureInfo.InvariantCulture)]),
        _ => new SqlType(SqlTypeKind.BuiltIn, Type.Name, []),
    };

    /// <summary>The type as the IR writes it.</summary>
    public override string ToString() => Text;
}
