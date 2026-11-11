namespace Haruspex.Ir;

/// <summary>Format refinements of scalar kinds. Dialect-neutral: they describe values, not SQL types.</summary>
public static class IrFormats
{
    /// <summary>An integer from 0 to 255, such as SQL Server's <c>tinyint</c>.</summary>
    public const string Uint8 = "uint8";

    /// <summary>A signed 16-bit integer.</summary>
    public const string Int16 = "int16";

    /// <summary>A signed 32-bit integer, which a JavaScript number holds exactly.</summary>
    public const string Int32 = "int32";

    /// <summary>A 64-bit integer: more than a JavaScript number holds exactly.</summary>
    public const string Int64 = "int64";

    /// <summary>An exact decimal, such as <c>decimal(19,2)</c>.</summary>
    public const string Decimal = "decimal";

    /// <summary>A binary floating-point number.</summary>
    public const string Float = "float";

    /// <summary>A date and time without an offset from UTC.</summary>
    public const string Local = "local";

    /// <summary>A date and time with an offset from UTC.</summary>
    public const string Offset = "offset";
}
