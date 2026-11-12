#pragma warning disable CA1720 // A family is named for the SQL values it holds, some of which share a name with a .NET type.

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The kind of value a built-in type holds, which decides the rules that apply to it: arithmetic over the numbers, length
/// over the strings, fractional seconds over the dates and times. Finer than Microsoft's categories ("Exact numerics",
/// "Character strings" and the rest), because bit, the integers, the decimals and money each follow rules of their own.
/// </summary>
public enum TypeFamily
{
    /// <summary><c>bit</c>: 0 or 1, an exact number that the server keeps apart from the integers in arithmetic.</summary>
    Bit,

    /// <summary><c>tinyint</c>, <c>smallint</c>, <c>int</c> and <c>bigint</c>.</summary>
    Integer,

    /// <summary><c>decimal</c> and <c>numeric</c>, of a precision and scale.</summary>
    Decimal,

    /// <summary><c>money</c> and <c>smallmoney</c>: exact, of four places.</summary>
    Money,

    /// <summary><c>float</c> and <c>real</c>.</summary>
    Approximate,

    /// <summary><c>char</c>, <c>varchar</c>, <c>nchar</c> and <c>nvarchar</c>.</summary>
    String,

    /// <summary><c>binary</c> and <c>varbinary</c>.</summary>
    Binary,

    /// <summary><c>date</c>, <c>time</c>, <c>datetime</c>, <c>smalldatetime</c>, <c>datetime2</c> and <c>datetimeoffset</c>.</summary>
    DateAndTime,

    /// <summary><c>uniqueidentifier</c>.</summary>
    Uuid,

    /// <summary><c>text</c>, <c>ntext</c> and <c>image</c>, the large-object types Microsoft deprecates.</summary>
    LargeObject,

    /// <summary><c>xml</c>.</summary>
    Xml,

    /// <summary><c>json</c>, from SQL Server 2025.</summary>
    Json,

    /// <summary><c>sql_variant</c>, which holds a value of another type.</summary>
    Variant,

    /// <summary><c>timestamp</c>, also called <c>rowversion</c>: a number the server writes on every change of a row.</summary>
    RowVersion,

    /// <summary><c>vector</c>, from SQL Server 2025.</summary>
    Vector,

    /// <summary><c>geography</c>, <c>geometry</c> and <c>hierarchyid</c>: system types implemented in .NET.</summary>
    Clr,
}
