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

    /// <summary>
    /// The bits of a <c>float</c>'s mantissa, <c>float(24)</c>: from 1 to 24 the type is <c>real</c>, from 25 to 53 <c>float</c>,
    /// so the argument picks the type and is not kept.
    /// </summary>
    MantissaBits,

    /// <summary>A number of dimensions: <c>vector(3)</c>.</summary>
    Dimensions,
}
