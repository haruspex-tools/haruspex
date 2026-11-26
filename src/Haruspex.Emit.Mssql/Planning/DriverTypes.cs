using System.Globalization;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>
/// The driver's type a parameter is sent as, from what the IR says of its type alone: its kind, format and facets. Sending the
/// declared type keeps the driver from choosing one by the JavaScript value (a number as <c>int</c> or <c>float</c>), and the
/// parameters' schema has already checked the value fits it, so the server keeps it as sent (<c>ParameterAcceptanceTests</c>).
/// </summary>
/// <remarks>
/// A date or time is sent as text, an ISO 8601 string that SQL Server converts to the parameter's type exactly, in any session
/// (<c>docs/decisions/0055-dates-and-times-are-sent-as-iso-8601-strings.md</c>): the driver's own date types would read it in the
/// process's time zone. <c>money</c> is sent as the decimal of its digits, which the server converts exactly; the driver's
/// <c>Money</c> cuts a fifth place, where the schema has refused one anyway.
/// </remarks>
internal static class DriverTypes
{
    /// <summary>The type <paramref name="type"/>'s value is sent as; null for one whose type isn't known, which the driver chooses.</summary>
    public static DriverType? Of(IrType type) => type switch
    {
        IrScalar { Kind: IrScalarKind.String } scalar => Text(scalar.Facets?.Length),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Uint8 } => Plain("TinyInt"),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Int16 } => Plain("SmallInt"),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Int32 } => Plain("Int"),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Int64 } => Plain("BigInt"),
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Decimal, Facets.Digits: { } digits } => new DriverType("Decimal", [Count(digits.Precision), Count(digits.Scale)]),

        // A float of either width is sent as float, which the server converts to real, as an assignment does.
        IrScalar { Kind: IrScalarKind.Number, Format: IrFormat.Float } => Plain("Float"),
        IrScalar { Kind: IrScalarKind.Boolean } => Plain("Bit"),
        IrScalar { Kind: IrScalarKind.Uuid } => Plain("UniqueIdentifier"),
        IrScalar { Kind: IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime } => new DriverType("VarChar", ["MAX"]),
        IrScalar { Kind: IrScalarKind.Bytes } scalar => Bytes(scalar.Facets?.Length),
        IrJsonText => new DriverType("NVarChar", ["MAX"]),

        // XML is sent as the driver's Xml, text the server parses (observed: MssqlDriverObservationTests).
        IrScalar { Kind: IrScalarKind.Xml } => Plain("Xml"),

        // A number of no known format or digits, a table-valued parameter, or a type not modelled: the driver chooses.
        _ => null,
    };

    /// <summary>A string of its declared length and unit: UTF-16 code units are <c>nvarchar</c>, bytes <c>varchar</c>; none is <c>max</c>.</summary>
    private static DriverType Text(IrLength? length) => length switch
    {
        null => new DriverType("NVarChar", ["MAX"]),
        { Unit: IrLengthUnit.Utf16 } => new DriverType(length.IsFixed ? "NChar" : "NVarChar", [Count(length.Value)]),
        _ => new DriverType(length.IsFixed ? "Char" : "VarChar", [Count(length.Value)]),
    };

    /// <summary>
    /// A binary value of its declared length, <c>VarBinary(n)</c>, or <c>VarBinary(MAX)</c> with none. A fixed length is sent as
    /// <c>VarBinary(n)</c> too, which the server pads to it as it converts any binary value: the driver's <c>Binary</c> takes a length
    /// at run time, but its type declarations (<c>@types/mssql</c>) give it none, so <c>sql.Binary(32)</c> doesn't compile. The
    /// parameters' schema has refused a longer value, which <c>VarBinary(n)</c> would fail with error 8016 (observed in
    /// <c>MssqlDriverObservationTests</c>).
    /// </summary>
    private static DriverType Bytes(IrLength? length) => length switch
    {
        null => new DriverType("VarBinary", ["MAX"]),
        _ => new DriverType("VarBinary", [Count(length.Value)]),
    };

    private static DriverType Plain(string name) => new(name, []);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
