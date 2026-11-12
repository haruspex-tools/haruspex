namespace Haruspex.Ir;

/// <summary>
/// A format refining a scalar kind. Dialect-neutral: it describes values, not SQL types. Its name in the IR's JSON is in
/// <see cref="IrWireNames"/>.
/// </summary>
public enum IrFormat
{
    /// <summary>An integer from 0 to 255, such as SQL Server's <c>tinyint</c>.</summary>
    Uint8,

    /// <summary>A signed 16-bit integer.</summary>
    Int16,

    /// <summary>A signed 32-bit integer, which a JavaScript number holds exactly.</summary>
    Int32,

    /// <summary>A 64-bit integer: more than a JavaScript number holds exactly.</summary>
    Int64,

    /// <summary>An exact decimal, such as <c>decimal(19,2)</c>.</summary>
    Decimal,

    /// <summary>A binary floating-point number.</summary>
    Float,

    /// <summary>A date and time without an offset from UTC.</summary>
    Local,

    /// <summary>A date and time with an offset from UTC.</summary>
    Offset,
}
