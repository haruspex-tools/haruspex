using System.Numerics;
using Haruspex.SqlServer.Model;

#pragma warning disable CA1720 // The catalogue names SQL Server's types, some of which share a name with a .NET type (int, decimal, float, char).

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// One of SQL Server's built-in data types, with what the server says about it: its family, its place in data type precedence,
/// its digits, its range, its arguments and their defaults. The catalogue is closed: every built-in type a column or a value
/// can have is a static instance here, listed in <see cref="All"/>, and no other can be made. Semantics and Inference read a
/// type's facts from here, and a type's name is compared as a string only in <see cref="Named"/>, the catalogue's parser.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every attribute is a claim, and each is checked against the server</b> by the differential tests'
/// <c>BuiltInTypeObservationTests</c>, an attribute per test: the name, the digits, the fractional seconds and the defaults as
/// the server describes the type; the range by converting its edges and one past them; the longest text by converting its
/// edges to text; the precedence, taken from Microsoft's "Data type precedence (Transact-SQL)", by the type
/// <c>COALESCE</c> gives every pair of types that converts. An attribute that doesn't apply is null, and a type Microsoft's
/// list leaves out (<c>json</c>, <c>vector</c> and the .NET system types) has no precedence: none is claimed.
/// </para>
/// <para>
/// <b>Not every type is modelled.</b> The catalogue lists every type so that what isn't modelled can be listed too. Which
/// types inference gives a value of is inference's to say (the table in <c>SqlTypes</c>); any other reaches the IR as
/// unknown, with a reason. <c>cursor</c> and <c>table</c> are left out: they are types of variables, never of a value.
/// </para>
/// <para>
/// <b>A sealed class with a private constructor, not an enum.</b> An enum would name the types, and each attribute would then
/// be a <c>switch</c> of its own somewhere, which is how the families came to be written out five times. Here a type's facts
/// are one row, and a new attribute is one more column.
/// </para>
/// </remarks>
public sealed class BuiltInType
{
    private BuiltInType(
        string name,
        TypeFamily family,
        int? precedence,
        TypeArgumentKind arguments = TypeArgumentKind.None,
        (int Precision, int Scale)? digits = null,
        BigInteger? least = null,
        BigInteger? greatest = null,
        int scaleOfRange = 0,
        int? fractionalSeconds = null,
        bool national = false,
        bool varying = false,
        int? longestText = null)
    {
        Name = name;
        Family = family;
        Precedence = precedence;
        Arguments = arguments;
        Digits = digits;
        Least = least is { } l ? ExactDecimal.Of(l, scaleOfRange) : null;
        Greatest = greatest is { } g ? ExactDecimal.Of(g, scaleOfRange) : null;
        FractionalSeconds = fractionalSeconds;
        IsNational = national;
        IsVarying = varying;
        LongestText = longestText;
    }

    // Precedence is the place in Microsoft's list, from 1, the highest, for user-defined types, to 30 for binary.

    /// <summary><c>bit</c>.</summary>
    public static readonly BuiltInType Bit = new("bit", TypeFamily.Bit, 19, digits: (1, 0), least: 0, greatest: 1, longestText: 1);

    /// <summary><c>tinyint</c>.</summary>
    public static readonly BuiltInType TinyInt = new("tinyint", TypeFamily.Integer, 18, digits: (3, 0), least: 0, greatest: byte.MaxValue, longestText: 3);

    /// <summary><c>smallint</c>.</summary>
    public static readonly BuiltInType SmallInt = new("smallint", TypeFamily.Integer, 17, digits: (5, 0), least: short.MinValue, greatest: short.MaxValue, longestText: 6);

    /// <summary><c>int</c>.</summary>
    public static readonly BuiltInType Int = new("int", TypeFamily.Integer, 16, digits: (10, 0), least: int.MinValue, greatest: int.MaxValue, longestText: 11);

    /// <summary><c>bigint</c>.</summary>
    public static readonly BuiltInType BigInt = new("bigint", TypeFamily.Integer, 15, digits: (19, 0), least: long.MinValue, greatest: long.MaxValue, longestText: 20);

    /// <summary><c>decimal</c>.</summary>
    public static readonly BuiltInType Decimal = new("decimal", TypeFamily.Decimal, 12, TypeArgumentKind.PrecisionAndScale);

    /// <summary><c>numeric</c>, the same as <c>decimal</c> but for its name, which the server keeps through arithmetic.</summary>
    public static readonly BuiltInType Numeric = new("numeric", TypeFamily.Decimal, 12, TypeArgumentKind.PrecisionAndScale);

    /// <summary><c>money</c>.</summary>
    public static readonly BuiltInType Money = new("money", TypeFamily.Money, 13, digits: (19, 4), least: long.MinValue, greatest: long.MaxValue, scaleOfRange: 4);

    /// <summary><c>smallmoney</c>.</summary>
    public static readonly BuiltInType SmallMoney = new("smallmoney", TypeFamily.Money, 14, digits: (10, 4), least: int.MinValue, greatest: int.MaxValue, scaleOfRange: 4);

    /// <summary><c>float</c>, 53 bits of mantissa; no range is claimed of an approximate number.</summary>
    public static readonly BuiltInType Float = new("float", TypeFamily.Approximate, 10);

    /// <summary><c>real</c>, 24 bits of mantissa.</summary>
    public static readonly BuiltInType Real = new("real", TypeFamily.Approximate, 11);

    /// <summary><c>char</c>: a fixed length in bytes.</summary>
    public static readonly BuiltInType Char = new("char", TypeFamily.String, 28, TypeArgumentKind.Length);

    /// <summary><c>varchar</c>: a varying length in bytes, or <c>max</c>.</summary>
    public static readonly BuiltInType VarChar = new("varchar", TypeFamily.String, 27, TypeArgumentKind.Length, varying: true);

    /// <summary><c>nchar</c>: a fixed length in UTF-16 code units.</summary>
    public static readonly BuiltInType NChar = new("nchar", TypeFamily.String, 26, TypeArgumentKind.Length, national: true);

    /// <summary><c>nvarchar</c>: a varying length in UTF-16 code units, or <c>max</c>.</summary>
    public static readonly BuiltInType NVarChar = new("nvarchar", TypeFamily.String, 25, TypeArgumentKind.Length, national: true, varying: true);

    /// <summary><c>binary</c>: a fixed length in bytes.</summary>
    public static readonly BuiltInType Binary = new("binary", TypeFamily.Binary, 30, TypeArgumentKind.Length);

    /// <summary><c>varbinary</c>: a varying length in bytes, or <c>max</c>.</summary>
    public static readonly BuiltInType VarBinary = new("varbinary", TypeFamily.Binary, 29, TypeArgumentKind.Length, varying: true);

    /// <summary><c>date</c>.</summary>
    public static readonly BuiltInType Date = new("date", TypeFamily.DateAndTime, 8, fractionalSeconds: 0);

    /// <summary><c>time</c>, of 0 to 7 fractional digits.</summary>
    public static readonly BuiltInType Time = new("time", TypeFamily.DateAndTime, 9, TypeArgumentKind.FractionalSeconds);

    /// <summary><c>datetime</c>: 3 fractional digits, rounded to 1/300 of a second.</summary>
    public static readonly BuiltInType DateTime = new("datetime", TypeFamily.DateAndTime, 6, fractionalSeconds: 3);

    /// <summary><c>smalldatetime</c>: to the minute.</summary>
    public static readonly BuiltInType SmallDateTime = new("smalldatetime", TypeFamily.DateAndTime, 7, fractionalSeconds: 0);

    /// <summary><c>datetime2</c>, of 0 to 7 fractional digits.</summary>
    public static readonly BuiltInType DateTime2 = new("datetime2", TypeFamily.DateAndTime, 5, TypeArgumentKind.FractionalSeconds);

    /// <summary><c>datetimeoffset</c>, of 0 to 7 fractional digits, with an offset from UTC.</summary>
    public static readonly BuiltInType DateTimeOffset = new("datetimeoffset", TypeFamily.DateAndTime, 4, TypeArgumentKind.FractionalSeconds);

    /// <summary><c>uniqueidentifier</c>: a GUID, 36 characters as text.</summary>
    public static readonly BuiltInType UniqueIdentifier = new("uniqueidentifier", TypeFamily.Uuid, 24, longestText: 36);

    /// <summary><c>text</c>, deprecated.</summary>
    public static readonly BuiltInType Text = new("text", TypeFamily.LargeObject, 21);

    /// <summary><c>ntext</c>, deprecated.</summary>
    public static readonly BuiltInType NText = new("ntext", TypeFamily.LargeObject, 20, national: true);

    /// <summary><c>image</c>, deprecated.</summary>
    public static readonly BuiltInType Image = new("image", TypeFamily.LargeObject, 22);

    /// <summary><c>xml</c>.</summary>
    public static readonly BuiltInType Xml = new("xml", TypeFamily.Xml, 3);

    /// <summary><c>json</c>, which Microsoft's list of precedence leaves out.</summary>
    public static readonly BuiltInType Json = new("json", TypeFamily.Json, null);

    /// <summary><c>sql_variant</c>.</summary>
    public static readonly BuiltInType SqlVariant = new("sql_variant", TypeFamily.Variant, 2);

    /// <summary><c>timestamp</c>, which a declaration may also call <c>rowversion</c>.</summary>
    public static readonly BuiltInType Timestamp = new("timestamp", TypeFamily.RowVersion, 23);

    /// <summary><c>vector</c>, of a number of dimensions, which Microsoft's list of precedence leaves out.</summary>
    public static readonly BuiltInType Vector = new("vector", TypeFamily.Vector, null, TypeArgumentKind.Dimensions);

    /// <summary><c>geography</c>, a .NET system type.</summary>
    public static readonly BuiltInType Geography = new("geography", TypeFamily.Clr, null);

    /// <summary><c>geometry</c>, a .NET system type.</summary>
    public static readonly BuiltInType Geometry = new("geometry", TypeFamily.Clr, null);

    /// <summary><c>hierarchyid</c>, a .NET system type.</summary>
    public static readonly BuiltInType HierarchyId = new("hierarchyid", TypeFamily.Clr, null);

    /// <summary>Every built-in type, in the order of Microsoft's list of precedence, then those it leaves out.</summary>
    public static IReadOnlyList<BuiltInType> All { get; } =
    [
        SqlVariant, Xml, DateTimeOffset, DateTime2, DateTime, SmallDateTime, Date, Time, Float, Real, Decimal, Numeric, Money,
        SmallMoney, BigInt, Int, SmallInt, TinyInt, Bit, NText, Text, Image, Timestamp, UniqueIdentifier, NVarChar, NChar,
        VarChar, Char, VarBinary, Binary, Json, Vector, Geography, Geometry, HierarchyId,
    ];

    /// <summary>The name <c>sys.types</c> gives it, in lower case: <c>datetime2</c>.</summary>
    public string Name { get; }

    /// <summary>The kind of value it holds.</summary>
    public TypeFamily Family { get; }

    /// <summary>
    /// Its place in data type precedence, as Microsoft lists it, from 1, the highest: of two values of different types, the
    /// one of higher precedence is converted to. Null for a type the list leaves out, of which none is claimed.
    /// </summary>
    public int? Precedence { get; }

    /// <summary>What its arguments say.</summary>
    public TypeArgumentKind Arguments { get; }

    /// <summary>
    /// The precision and scale of a type whose digits are fixed: bit, the integers and money. Beside a decimal, a value of
    /// the type is a decimal of these digits. Null for any other type.
    /// </summary>
    public (int Precision, int Scale)? Digits { get; }

    /// <summary>The least value of an exact number of fixed digits, or null.</summary>
    public ExactDecimal? Least { get; }

    /// <summary>The greatest value of an exact number of fixed digits, or null.</summary>
    public ExactDecimal? Greatest { get; }

    /// <summary>
    /// The fractional digits of a date or time type that takes no argument for them: <c>datetime</c> 3, <c>smalldatetime</c>
    /// and <c>date</c> 0. Null for any other type: <c>datetime2</c>, <c>time</c> and <c>datetimeoffset</c> are given theirs.
    /// </summary>
    public int? FractionalSeconds { get; }

    /// <summary>Whether its text is Unicode: <c>nchar</c>, <c>nvarchar</c>, <c>ntext</c>.</summary>
    public bool IsNational { get; }

    /// <summary>Whether its length varies, and may be <c>max</c>: <c>varchar</c>, <c>nvarchar</c>, <c>varbinary</c>.</summary>
    public bool IsVarying { get; }

    /// <summary>
    /// How many characters its longest value takes as text, for a type whose every value has a length (a sign included):
    /// <c>int</c> 11, <c>uniqueidentifier</c> 36. Null for any other type.
    /// </summary>
    public int? LongestText { get; }

    /// <summary>The length of a string or binary type declared without one: <c>char</c> is <c>char(1)</c>.</summary>
    public static int LengthDeclared => 1;

    /// <summary>The length of a string or binary type written without one in <c>CAST</c> or <c>CONVERT</c>: 30.</summary>
    public static int LengthInConversion => 30;

    /// <summary>The longest a <c>char</c>, <c>varchar</c>, <c>binary</c> or <c>varbinary</c> can be without max, in bytes.</summary>
    public static int MostBytes => 8000;

    /// <summary>The precision and scale of <c>decimal</c> or <c>numeric</c> declared without them: (18, 0).</summary>
    public static (int Precision, int Scale) DigitsDeclared => (18, 0);

    /// <summary>The fractional digits of <c>time</c>, <c>datetime2</c> or <c>datetimeoffset</c> declared without them: 7.</summary>
    public static int FractionalSecondsDeclared => 7;

    /// <summary>The longest a value of the type can be without max: in bytes, or in characters for a national type.</summary>
    public int MostLength => IsNational ? MostBytes / 2 : MostBytes;

    /// <summary>
    /// The built-in type of this name, as <c>sys.types</c> names it, in any case, with <c>rowversion</c> as <c>timestamp</c>;
    /// null for any other name. The one place a type's name is compared.
    /// </summary>
    public static BuiltInType? Named(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            return Timestamp;
        return All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The built-in type a declaration names: a built-in type by its keyword, <c>xml</c>, or a .NET system type by its name,
    /// unqualified or in <c>sys</c>. Null for an alias or a user's type, which <see cref="SchemaRules.ResolveType"/> resolves.
    /// </summary>
    public static BuiltInType? Of(SqlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Kind switch
        {
            SqlTypeKind.BuiltIn => Named(type.Name),
            SqlTypeKind.Xml => Xml,
            _ when type.Schema is null || type.Schema.Equals("sys", StringComparison.OrdinalIgnoreCase)
                => Named(type.Name) is { Family: TypeFamily.Clr } clr ? clr : null,
            _ => null,
        };
    }

    /// <summary>
    /// A system alias type resolved: <c>sysname</c> is <c>nvarchar(128)</c>, NOT NULL unless a column says NULL. Null for any
    /// other name. System types are found before a user's for an unqualified name.
    /// </summary>
    public static ResolvedType? SystemAlias(SqlType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type is { Kind: SqlTypeKind.Named } && type.Name.Equals("sysname", StringComparison.OrdinalIgnoreCase)
            && (type.Schema is null || type.Schema.Equals("sys", StringComparison.OrdinalIgnoreCase))
            ? new ResolvedType(new SqlType(SqlTypeKind.BuiltIn, NVarChar.Name, ["128"]), "sysname", DeclaredNullability.NotNull)
            : null;
    }

    /// <summary>
    /// Whether it ranks at least as high as <paramref name="other"/> in data type precedence, so that of two values of these
    /// types the other converts to it. Throws for a type of no precedence, of which nothing is claimed.
    /// </summary>
    public bool RanksAtLeast(BuiltInType other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Precedence is not { } mine || other.Precedence is not { } theirs)
            throw new InvalidOperationException($"No precedence is claimed for {(Precedence is null ? this : other).Name}.");
        return mine <= theirs;
    }

    /// <summary>Whether it is one of <paramref name="types"/>.</summary>
    public bool IsAnyOf(params ReadOnlySpan<BuiltInType> types)
    {
        foreach (var type in types)
        {
            if (ReferenceEquals(this, type))
                return true;
        }

        return false;
    }

    /// <summary>Its name.</summary>
    public override string ToString() => Name;
}
