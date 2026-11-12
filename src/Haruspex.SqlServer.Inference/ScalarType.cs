using System.Globalization;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A built-in scalar type as SQL Server types an expression: what <c>COALESCE</c>, conversions, string building and
/// decimal arithmetic compute from, and what they give. The server's rules over it are in the folder Typing, one class
/// per rule (<see cref="Typing.TypeChain"/>, <see cref="Typing.Arithmetic"/>, <see cref="Typing.Conversions"/>).
/// </summary>
/// <param name="Name">The built-in name in lower case.</param>
/// <param name="Length">A string's length (bytes for char and varchar, characters for nchar and nvarchar); -1 for max.</param>
/// <param name="Precision">An exact number's precision, or a date or time type's fractional digits.</param>
/// <param name="Scale">An exact number's scale.</param>
/// <remarks>
/// A type has no integer digits of its own beyond its name's: the digits an integer literal counts are an operand's, so they
/// travel beside the type, in an <see cref="Operand"/>, into the rules that read them.
/// </remarks>
internal sealed record ScalarType(string Name, int Length = 0, int Precision = 0, int Scale = 0)
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

    internal static int Rank(string integer) => integer switch
    {
        "bit" => 0,
        "tinyint" => 1,
        "smallint" => 2,
        "int" => 3,
        "bigint" => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(integer), integer, "Not an integer type."),
    };

    /// <summary>The integer digits a type's values can have: <c>int</c> 10, <c>smallint</c> 5.</summary>
    internal static int IntegerDigits(string integer) => integer switch
    {
        "bit" => 1,
        "tinyint" => 3,
        "smallint" => 5,
        "int" => 10,
        "bigint" => 19,
        _ => throw new ArgumentOutOfRangeException(nameof(integer), integer, "Not an integer type."),
    };

    /// <summary>Whether it is named numeric rather than decimal: a numeric column or a decimal literal.</summary>
    internal bool IsNumericNamed => Name == "numeric";

    /// <summary>
    /// Data type precedence, highest first, for the types modelled here (Microsoft's "Data type precedence"): what a column
    /// of a set operation, <c>COALESCE</c>, <c>CASE</c>, <c>IIF</c> and <c>CHOOSE</c> take when their values' types differ.
    /// </summary>
    internal static int PrecedenceRank(string name) => name switch
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
}
