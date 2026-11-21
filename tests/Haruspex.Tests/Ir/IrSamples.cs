using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// An IR document built by hand that holds every type node and every optional property of the format at least once, so a
/// test of the reader, the writer or the schema that passes on it has met the whole format. Its names are invented: a shop's
/// orders.
/// </summary>
internal static class IrSamples
{
    private static readonly IrFactSource FromType = new(IrFactSourceKind.Type, "nvarchar(40)");

    private static readonly IrNullReason Column = new(IrNullRule.NullableColumn, "sales.orders.note");

    /// <summary>A column a field is read from (0.26), with no SQL type, as a column of a type the IR doesn't type would have.</summary>
    private static readonly IrSourceColumn OrderId = new("sales.orders.order_id", null, "tables/orders.sql:2:5");

    private static readonly IrSourceColumn Channel = new("sales.orders.channel", "varchar(10)", "tables/orders.sql:6:5");

    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static readonly IrScalar Note = new(IrScalarKind.String, null, "nvarchar(40)", new IrFacets(Length: new IrLength(40, IsFixed: false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, FromType)));

    /// <summary>The document.</summary>
    public static IrDocument Everything { get; } = new(
        new IrSource("tsql")
        {
            Session = [new IrSessionOption("ansiWarnings", true), new IrSessionOption("concatNullYieldsNull", false)],
            Package = new IrPackage("shop.dacpac", Hash('a')),
            References =
            [
                new IrPackageReference("master.dacpac", null, "master"),
                new IrPackageReference("ledger.dacpac", Hash('c'), "$(Ledger)"),
                new IrPackageReference("catalogue.dacpac", Hash('b'), null),
            ],
        },
        [
            new IrObject(
                "sales.get_order",
                "GetOrder",
                IrObjectKind.Procedure,
                "procedures/get_order.sql:1:1",
                [
                    new IrParameter("@order_id", Int, HasDefault: false, IsOutput: false),
                    new IrParameter("@total", new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(7,2)"), HasDefault: true, IsOutput: true),
                ],
                [
                    new IrResult(IrEncoding.Json, new IrRef("OrderRow"), [new IrNullReason(IrNullRule.NoRows, "procedures/get_order.sql:4:5")])
                    {
                        Name = "order",
                        Cardinality = IrCardinality.AtMostOne,
                    },
                    new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("LineRow")), []) { Name = "lines" },
                ])
            {
                ReturnsCode = true,
            },
            new IrObject("sales.run_report", "RunReport", IrObjectKind.Procedure, "procedures/run_report.sql:1:1", [], null),
            new IrObject(
                "sql/find_orders.sql",
                "FindOrders",
                IrObjectKind.Query,
                "sql/find_orders.sql:1:1",
                [
                    new IrParameter("@customer_id", Int, HasDefault: false, IsOutput: false),
                    new IrParameter("@since", new IrUnknownType(IrUnknownReason.UndecidedParameter), HasDefault: false, IsOutput: false),
                ],
                [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("FindOrdersRow")), [])])
            {
                Text = "select order_id from sales.orders\nwhere customer_id = @customer_id and (@since is null or @since < 1);\n",
            },
        ],
        new Dictionary<string, IrObjectType>(StringComparer.Ordinal)
        {
            ["OrderRow"] = new(
            [
                new IrField(
                    "note",
                    new IrScalar(IrScalarKind.String, null, "nvarchar(40)", new IrFacets(Length: new IrLength(40, IsFixed: false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, FromType))),
                    IrPresence.OmittedWhenNull,
                    [Column]),
                new IrField(
                    "code",
                    new IrScalar(IrScalarKind.String, null, "char(3)", new IrFacets(
                        Length: new IrLength(3, IsFixed: true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, new IrFactSource(IrFactSourceKind.Type, "char(3)")),
                        MinLength: new IrMinLength(1, new IrFactSource(IrFactSourceKind.Check, "ck_orders_code")),
                        Values: new IrValueSet(
                            [new IrValue("new", IsString: true), new IrValue("old", IsString: true)],
                            IrValueMatch.Collation,
                            [new IrFactSource(IrFactSourceKind.Check, "ck_orders_code"), new IrFactSource(IrFactSourceKind.Expression, "procedures/get_order.sql:3:9")]))),
                    IrPresence.Always,
                    []),
                new IrField(
                    "state",
                    new IrScalar(IrScalarKind.String, null, "varchar(8)", new IrFacets(Values: new IrValueSet(
                        [new IrValue("due", IsString: true), new IrValue("paid", IsString: true)],
                        IrValueMatch.Canonical,
                        [new IrFactSource(IrFactSourceKind.Check, "ck_orders_state")],
                        "Latin1_General_100_CI_AS.codepage"))),
                    IrPresence.Always,
                    []),
                new IrField(
                    "total",
                    new IrScalar(IrScalarKind.Number, IrFormat.Decimal, "decimal(7,2)", new IrFacets(
                        Digits: new IrDigits(7, 2, new IrFactSource(IrFactSourceKind.Type, "decimal(7,2)")),
                        Min: new IrBound("-1", Inclusive: false, new IrFactSource(IrFactSourceKind.Check, "ck_orders_total")),
                        Max: new IrBound("99999.99", Inclusive: true, new IrFactSource(IrFactSourceKind.Type, "decimal(7,2)")),
                        Values: new IrValueSet([new IrValue("0", IsString: false), new IrValue("-2.5", IsString: false)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Engine, "$action")]))),
                    IrPresence.Always,
                    []),
                new IrField("lines", new IrArray(new IrRef("LineRow")) { ItemsNullableBecause = [Column], MinItems = 1, MaxItems = 3 }, IrPresence.Always, []),
                new IrField("pair", new IrTuple([new IrElement(Int, []), new IrElement(new IrScalar(IrScalarKind.Boolean, null, "bit"), [Column])]), IrPresence.Always, []),
                new IrField("tags", new IrRecord(new IrScalar(IrScalarKind.Date, null, "date"), [Column]), IrPresence.Always, []),
                new IrField("plain", new IrRecord(new IrScalar(IrScalarKind.Uuid, null, "uniqueidentifier"), []), IrPresence.Always, []),
                new IrField("either", new IrUnion([Int, new IrScalar(IrScalarKind.Time, null, "time(0)")]), IrPresence.Always, []),
                new IrField("payload", new IrJsonText(new IrRef("LineRow")), IrPresence.Always, []),
                new IrField("stored", new IrJsonText(), IrPresence.Always, []),
                new IrField("settings", new IrImport("OrderSettingsSchema", "./settings.js"), IrPresence.Always, []),
                new IrField("when", new IrScalar(IrScalarKind.Datetime, IrFormat.Offset, "datetimeoffset"), IrPresence.Always, []),
                new IrField("odd", new IrUnknownType(IrUnknownReason.UnsupportedExpression), IrPresence.Always, [new IrNullReason(IrNullRule.Unknown, "procedures/get_order.sql:5:9")]),
            ])
            {
                Identity = new IrTypeIdentity(IrTypeOwner.Procedure, "sales.get_order", "results[0]"),
                NameDeclared = true,
            },
            ["LineRow"] = new([new IrField("sku", Int, IrPresence.Always, []), new IrField("parts", new IrArray(new IrRef("LineRow")), IrPresence.Always, [])])
            {
                Identity = new IrTypeIdentity(IrTypeOwner.Function, "sales.line_tree", "<sales.line_tree>"),
                Recursive = true,
            },
            ["Bare"] = new([]),
            ["FindOrdersRow"] = new([new IrField("order_id", Int, IrPresence.Always, [])])
            {
                Identity = new IrTypeIdentity(IrTypeOwner.Query, "sql/find_orders.sql", "results[0][]"),
            },
        },
        [
            new IrUnknown("sales.get_order", "results[0].odd", IrUnknownReason.UnsupportedExpression, "An expression that is not modelled yet.", "procedures/get_order.sql:5:9"),
            new IrUnknown("sales.run_report", "results", IrUnknownReason.DynamicSql, "Dynamic SQL.", "procedures/run_report.sql:2:5"),
            new IrUnknown("sql/find_orders.sql", "parameters.@since", IrUnknownReason.UndecidedParameter, "Its uses don't decide its type.", "sql/find_orders.sql:2:42"),
        ])
    {
        Folds = new Dictionary<string, IrFold>
        {
            ["Latin1_General_100_CI_AS.codepage"] = new("\u0000\u00ad", new Dictionary<string, string> { ["A"] = "Aa", ["AE"] = "\u00c6\u00e6", [" "] = " " }.ToEquatableMap(c => c.Key, c => c.Value)),
        }.ToEquatableMap(f => f.Key, f => f.Value),
        Overrides =
        [
            new IrOverride("sales.get_order", "results[0].settings", IrOverrideSetting.Type, "Settings are checked by the application."),
            new IrOverride("sales.get_order", "results", IrOverrideSetting.Results, null) { Via = "sales.run_report" },
        ],
    };

    /// <summary>
    /// <see cref="Everything"/> combined with a baseline (0.24): its source names the baseline's, a key one of the two databases
    /// lacks may be left out whatever its value, and a result set only one of them returns may be missing.
    /// </summary>
    public static IrDocument Combined { get; } = Everything with
    {
        Source = Everything.Source with { Baseline = new IrSource("tsql") { Package = new IrPackage("shop-release.dacpac", Hash('d')) } },
        Objects =
        [
            .. Everything.Objects.Select(o => o.Id != "sales.get_order" ? o : o with
            {
                Results = [.. o.Results!, new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("FindOrdersRow")), [new IrNullReason(IrNullRule.NotInBoth, "procedures/get_order.sql:9:5")]) { Name = "notes" }],
            }),
        ],
        Types = new Dictionary<string, IrObjectType>(Everything.Types)
        {
            ["FindOrdersRow"] = Everything.Types["FindOrdersRow"] with
            {
                Fields = [.. Everything.Types["FindOrdersRow"].Fields, new IrField("channel", new IrScalar(IrScalarKind.String, null, "varchar(10)"), IrPresence.Optional, [Column])],
            },
        },
    };

    /// <summary>
    /// <see cref="Combined"/> saying where its fields came from (0.25): the columns each is read from, and how each database gave it,
    /// so that it holds every property the format has.
    /// </summary>
    public static IrDocument Derived { get; } = Combined with
    {
        Source = Combined.Source with
        {
            Path = "database",
            Files = new Dictionary<string, string> { ["tables/orders.sql"] = Hash('e') }.ToEquatableMap(f => f.Key, f => f.Value),
        },
        Types = Combined.Types.ToDictionary(t => t.Key, t => t.Key != "FindOrdersRow" ? t.Value : t.Value with
        {
            Fields =
            [
                .. t.Value.Fields.Select(f => f.Name == "channel"
                    ? f with
                    {
                        From = [Channel],
                        Sides = new IrFieldSides(null, new IrFieldSide(IrPresence.Always, [Column], "varchar(10)") { From = [Channel] }),
                    }
                    : f with
                    {
                        From = [OrderId],
                        Sides = new IrFieldSides(IrFieldSide.Of(f) with { From = [OrderId] }, IrFieldSide.Of(f) with { From = [OrderId] }),
                    }),
            ],
        }),
    };

    /// <summary>
    /// <see cref="Derived"/> with a table-valued parameter (0.26): its table type's definition, with a column of every kind,
    /// and its row, so that it holds every property of the format.
    /// </summary>
    public static IrDocument Whole { get; } = Derived with
    {
        Objects =
        [
            .. Derived.Objects.Select(o => o.Id != "sales.get_order" ? o : o with
            {
                Parameters = [.. o.Parameters, new IrParameter("@lines", new IrArray(new IrRef("LineListRow")), HasDefault: true, IsOutput: false) { TableType = "sales.line_list" }],
            }),
        ],
        Types = new Dictionary<string, IrObjectType>(Derived.Types)
        {
            ["LineListRow"] = new([new IrField("sku", Int, IrPresence.Always, []), new IrField("note", Note, IrPresence.Always, [new IrNullReason(IrNullRule.NullableColumn, "sales.line_list.note")])])
            {
                Identity = new IrTypeIdentity(IrTypeOwner.TableType, "sales.line_list", "<sales.line_list>"),
            },
        },
        TableTypes = new Dictionary<string, IrTableType>
        {
            ["sales.line_list"] = new(
                "sales",
                "line_list",
                [
                    new IrTableColumn("sku", "int", Nullable: false) { Type = Int },
                    new IrTableColumn("note", "nvarchar(40)", Nullable: true) { Type = Note, Collation = "Latin1_General_100_CI_AS" },
                    new IrTableColumn("serial", "int", Nullable: false) { Type = Int, Generated = IrColumnGeneration.Identity },
                    new IrTableColumn("loud", null, Nullable: true) { Generated = IrColumnGeneration.Computed },
                ],
                "types/line_list.sql:1:1")
            {
                Keys = [new IrTableKey(["sku"]) { IgnoresDuplicates = true }, new IrTableKey(["serial"])],
                Checks = ["sku > 0"],
            },
        }.ToEquatableMap(t => t.Key, t => t.Value),
    };

    /// <summary>
    /// <see cref="Whole"/> with binary values (0.27): a parameter of no length, a field of a fixed length and one of a maximum, so
    /// that it holds every kind of scalar the format has.
    /// </summary>
    public static IrDocument Binary { get; } = Whole with
    {
        Objects =
        [
            .. Whole.Objects.Select(o => o.Id != "sales.get_order" ? o : o with
            {
                Parameters = [.. o.Parameters, new IrParameter("@photo", new IrScalar(IrScalarKind.Bytes, null, "varbinary(max)"), HasDefault: true, IsOutput: false)],
            }),
        ],
        Types = Whole.Types.ToDictionary(t => t.Key, t => t.Key switch
        {
            "OrderRow" => t.Value with { Fields = [.. t.Value.Fields, new IrField("signature", Bytes("binary(32)", 32, isFixed: true), IrPresence.OmittedWhenNull, [Column])] },
            "LineRow" => t.Value with { Fields = [.. t.Value.Fields, new IrField("thumbnail", Bytes("varbinary(8000)", 8000, isFixed: false), IrPresence.Always, [])] },
            _ => t.Value,
        }),
    };

    /// <summary>
    /// <see cref="Binary"/> with a column declared to hold JSON of a schema (0.28): the declaration and the files it read, a claim
    /// that an object reads it, any JSON and any JSON object or array, and a string whose value set and minimum length a
    /// declaration states, so that it holds every property and every type node the format has.
    /// </summary>
    public static IrDocument Declared { get; } = Binary with
    {
        Types = Binary.Types.ToDictionary(t => t.Key, t => t.Key != "OrderRow" ? t.Value : t.Value with
        {
            Fields =
            [
                .. t.Value.Fields,
                new IrField("extras", new IrJsonText(new IrAnyJson()), IrPresence.Always, [Column]),
                new IrField("live", new IrAnyJson(ObjectOrArray: true), IrPresence.OmittedWhenNull, [new IrNullReason(IrNullRule.JsonNullOrAbsent, "procedures/get_order.sql:6:9")]),
                new IrField("mode", new IrScalar(IrScalarKind.String, null, null, new IrFacets(
                    MinLength: new IrMinLength(1, new IrFactSource(IrFactSourceKind.Declared, "sales.orders.settings")),
                    Values: new IrValueSet([new IrValue("fast", IsString: true), new IrValue("slow", IsString: true)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Declared, "sales.orders.settings")]))), IrPresence.Optional, []),
            ],
        }),
        Columns =
        [
            new IrColumnDeclaration(
                "sales.orders.settings",
                "schemas/order-settings.schema.json",
                "/$defs/settings",
                new Dictionary<string, string> { ["schemas/order-settings.schema.json"] = Hash('a'), ["schemas/common.schema.json"] = Hash('b') }.ToEquatableMap(f => f.Key, f => f.Value)),
        ],
        Overrides = [.. Binary.Overrides, new IrOverride("sales.get_order", "results", IrOverrideSetting.ColumnJson, null) { Via = "sales.orders.settings" }],
    };

    /// <summary>A binary value of a length its type gives.</summary>
    public static IrScalar Bytes(string sql, int length, bool isFixed) =>
        new(IrScalarKind.Bytes, null, sql, new IrFacets(Length: new IrLength(length, isFixed, IrLengthUnit.Bytes, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, sql))));

    /// <summary>Every type node in a document, nested ones included, each once per place it appears.</summary>
    public static IEnumerable<IrType> TypeNodes(IrDocument document) =>
        document.Objects.SelectMany(o => o.Parameters.Select(p => p.Type).Concat(o.Results?.Select(r => r.Type) ?? []))
            .Concat(document.Types.Values.SelectMany(t => t.Fields.Select(f => f.Type)))
            .SelectMany(IrTypeTree.Within);

    /// <summary>A SHA-256 as the IR writes one, made of one digit: a sample needs its form, not a real file's.</summary>
    private static string Hash(char digit) => new(digit, 64);
}
