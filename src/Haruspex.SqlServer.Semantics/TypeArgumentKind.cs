namespace Haruspex.SqlServer.Semantics;

/// <summary>What a built-in type's arguments say, as in <c>varchar(10)</c> or <c>decimal(19,2)</c>.</summary>
public enum TypeArgumentKind
{
    /// <summary>It takes none: <c>int</c>, <c>date</c>.</summary>
    None,

    /// <summary>A length, or <c>max</c> for a varying type: <c>nvarchar(20)</c>, <c>varbinary(max)</c>.</summary>
    Length,

    /// <summary>A precision and a scale: <c>decimal(19,2)</c>.</summary>
    PrecisionAndScale,

    /// <summary>The digits of a fraction of a second: <c>datetime2(3)</c>.</summary>
    FractionalSeconds,

    /// <summary>A number of dimensions: <c>vector(3)</c>.</summary>
    Dimensions,
}
