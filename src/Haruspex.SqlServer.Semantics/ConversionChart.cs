namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// SQL Server's conversion chart: whether a value of one built-in type converts to another implicitly, only explicitly, or
/// not at all, as Microsoft's chart in "Data type conversion (Database Engine)" shows it. Every cell was read from the server
/// (<c>CAST</c> for explicit, <c>ISNULL</c>'s second argument for implicit) and is checked by
/// <c>The_conversion_chart_is_what_the_server_allows_implicitly_explicitly_or_not_at_all</c>, so a cell that differs from the
/// published chart is the server's: <c>timestamp</c> converts implicitly to <c>varchar</c> and <c>char</c> but not to
/// <c>nvarchar</c> or <c>nchar</c>, and <c>json</c> to a string only explicitly.
/// </summary>
/// <remarks>
/// Whether a conversion that is allowed keeps every value, without an overflow, is a different question the chart doesn't
/// answer: Inference's table of conversions does, for the types it models.
/// </remarks>
public static class ConversionChart
{
    private static readonly BuiltInType[] Exact =
    [
        BuiltInType.Decimal, BuiltInType.Numeric, BuiltInType.Money, BuiltInType.SmallMoney, BuiltInType.BigInt, BuiltInType.Int,
        BuiltInType.SmallInt, BuiltInType.TinyInt, BuiltInType.Bit,
    ];

    private static readonly BuiltInType[] Numbers = [BuiltInType.Float, BuiltInType.Real, .. Exact];

    private static readonly BuiltInType[] NewDatesAndTimes = [BuiltInType.DateTimeOffset, BuiltInType.DateTime2, BuiltInType.Date, BuiltInType.Time];

    private static readonly BuiltInType[] DatesAndTimes = [.. NewDatesAndTimes, BuiltInType.DateTime, BuiltInType.SmallDateTime];

    private static readonly BuiltInType[] Strings = [BuiltInType.NVarChar, BuiltInType.NChar, BuiltInType.VarChar, BuiltInType.Char];

    private static readonly BuiltInType[] Binaries = [BuiltInType.VarBinary, BuiltInType.Binary];

    private static readonly BuiltInType[] LargeObjects = [BuiltInType.NText, BuiltInType.Text, BuiltInType.Image];

    private static readonly BuiltInType[] Clr = [BuiltInType.Geography, BuiltInType.Geometry, BuiltInType.HierarchyId];

    /// <summary>A row of the chart: from one type, the targets of each kind it lists, and the kind of every other target.</summary>
    private sealed record Row(
        BuiltInType From,
        BuiltInType[]? Implicit = null,
        BuiltInType[]? Explicit = null,
        BuiltInType[]? Refused = null,
        ConversionKind Otherwise = ConversionKind.Refused);

    private static readonly Row[] Rows =
    [
        new(BuiltInType.SqlVariant, Implicit: [BuiltInType.SqlVariant], Explicit: [.. Numbers, .. DatesAndTimes, .. Strings, .. Binaries, BuiltInType.UniqueIdentifier], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Xml, Implicit: [BuiltInType.Xml], Explicit: [.. Strings, .. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.DateTimeOffset, Implicit: [.. DatesAndTimes, .. Strings, BuiltInType.SqlVariant], Explicit: [.. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.DateTime2, Implicit: [.. DatesAndTimes, .. Strings, BuiltInType.SqlVariant], Explicit: [.. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.DateTime, Explicit: [.. Numbers, .. Binaries, BuiltInType.Timestamp], Refused: [.. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.SmallDateTime, Explicit: [.. Numbers, .. Binaries, BuiltInType.Timestamp], Refused: [.. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Date, Implicit: [.. Strings, BuiltInType.SqlVariant, BuiltInType.DateTimeOffset, BuiltInType.DateTime2, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.Date], Explicit: [.. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Time, Implicit: [.. Strings, BuiltInType.SqlVariant, BuiltInType.DateTimeOffset, BuiltInType.DateTime2, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.Time], Explicit: [.. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Float, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.Timestamp, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Real, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.Timestamp, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Decimal, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Numeric, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Money, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.SmallMoney, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.BigInt, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Int, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.SmallInt, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.TinyInt, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Bit, Refused: [.. NewDatesAndTimes, .. LargeObjects, .. Clr, BuiltInType.Xml, BuiltInType.UniqueIdentifier, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.NText, Implicit: [.. Strings, BuiltInType.Xml, BuiltInType.NText, BuiltInType.Text], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Text, Implicit: [.. Strings, BuiltInType.Xml, BuiltInType.NText, BuiltInType.Text], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Image, Implicit: [.. Binaries, BuiltInType.Image, BuiltInType.Timestamp], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Timestamp, Implicit: [.. Exact, .. Binaries, BuiltInType.DateTime, BuiltInType.SmallDateTime, BuiltInType.Image, BuiltInType.Timestamp, BuiltInType.VarChar, BuiltInType.Char], Otherwise: ConversionKind.Refused),
        new(BuiltInType.UniqueIdentifier, Implicit: [.. Strings, .. Binaries, BuiltInType.SqlVariant, BuiltInType.UniqueIdentifier], Otherwise: ConversionKind.Refused),
        new(BuiltInType.NVarChar, Explicit: [.. Binaries, BuiltInType.Timestamp], Refused: [BuiltInType.Image], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.NChar, Explicit: [.. Binaries, BuiltInType.Timestamp], Refused: [BuiltInType.Image], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.VarChar, Explicit: [.. Binaries, BuiltInType.Timestamp], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Char, Explicit: [.. Binaries, BuiltInType.Timestamp], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.VarBinary, Explicit: [.. NewDatesAndTimes], Refused: [BuiltInType.Float, BuiltInType.Real, BuiltInType.NText, BuiltInType.Text, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Binary, Explicit: [.. NewDatesAndTimes], Refused: [BuiltInType.Float, BuiltInType.Real, BuiltInType.NText, BuiltInType.Text, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Implicit),
        new(BuiltInType.Json, Implicit: [BuiltInType.Json, BuiltInType.Vector], Explicit: [.. Strings], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Vector, Implicit: [.. Strings, BuiltInType.Json, BuiltInType.Vector], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Geography, Implicit: [BuiltInType.Geography], Explicit: [.. Strings, .. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.Geometry, Implicit: [BuiltInType.Geometry], Explicit: [.. Strings, .. Binaries], Otherwise: ConversionKind.Refused),
        new(BuiltInType.HierarchyId, Implicit: [BuiltInType.HierarchyId], Explicit: [.. Strings, .. Binaries], Otherwise: ConversionKind.Refused),
    ];

    private static readonly Dictionary<BuiltInType, Row> ByFrom = Rows.ToDictionary(r => r.From);

    /// <summary>How a value of <paramref name="from"/> converts to <paramref name="to"/>.</summary>
    public static ConversionKind Of(BuiltInType from, BuiltInType to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var row = ByFrom[from];
        if (row.Implicit?.Contains(to) == true)
            return ConversionKind.Implicit;
        if (row.Explicit?.Contains(to) == true)
            return ConversionKind.Explicit;
        if (row.Refused?.Contains(to) == true)
            return ConversionKind.Refused;
        return row.Otherwise;
    }
}
