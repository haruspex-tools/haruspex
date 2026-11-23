using System.Text.Json.Nodes;
using Haruspex.Emit.JsonSchema;
using Haruspex.Emit.Zod;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>
/// What the generated schemas decide, run in Node against the pinned Zod, and the JSON Schema document's definitions against Ajv
/// (<c>tests/emit-check/src/verdicts.mjs</c>): each accepts
/// what SQL Server and the mssql driver hand over and refuses the forms they never do. A rowset's <c>bigint</c> arrives as the
/// text of its digits (observed: <c>MssqlDriverObservationTests.A_rowset_bigint_arrives_as_the_text_of_its_digits</c>), and a date
/// or time in JSON as <c>FOR JSON</c> writes its type; each accepted string below is one <c>ForJsonObservationTests</c> observed the
/// server write. The snapshots show what the module says; this shows it holds, so that a check loosened by a change fails here.
/// </summary>
public class ZodVerdictTests
{
    private static readonly IrFactSource Type = new(IrFactSourceKind.Type, "bigint");
    private static readonly IrFactSource Check = new(IrFactSourceKind.Check, "dbo.ck");

    /// <summary>Each case's type, by name: a row of one key, <c>v</c>, of this scalar, in this encoding.</summary>
    private static readonly Dictionary<string, (IrEncoding Encoding, IrScalar Scalar)> Types = new(StringComparer.Ordinal)
    {
        ["BigRow"] = (IrEncoding.Rowset, BigInt(new IrBound("-9223372036854775808", true, Type), new IrBound("9223372036854775807", true, Type))),
        ["AtLeastZeroRow"] = (IrEncoding.Rowset, BigInt(new IrBound("0", true, Check), new IrBound("9223372036854775807", true, Type))),
        ["BetweenRow"] = (IrEncoding.Rowset, BigInt(new IrBound("2.5", false, Check), new IrBound("100", false, Check))),
        ["DateRow"] = (IrEncoding.Json, DateTime(IrScalarKind.Date, null, "date")),
        ["Time0Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Time, null, "time(0)")),
        ["Time3Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Time, null, "time(3)")),
        ["Datetime20Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, "datetime2(0)")),
        ["Datetime23Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, "datetime2(3)")),
        ["Datetime27Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, "datetime2(7)")),
        ["Offset0Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Offset, "datetimeoffset(0)")),
        ["Offset3Row"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Offset, "datetimeoffset(3)")),
        ["DatetimeRow"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, "datetime")),
        ["SmallRow"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, "smalldatetime")),
        ["DeclaredRow"] = (IrEncoding.Json, DateTime(IrScalarKind.Datetime, IrFormat.Local, null)),
    };

    private static IrScalar BigInt(IrBound min, IrBound max) => new(IrScalarKind.Number, IrFormat.Int64, "bigint", new IrFacets(Min: min, Max: max));

    private static IrScalar DateTime(IrScalarKind kind, IrFormat? format, string? sql) => new(kind, format, sql);

    private static readonly (string Type, JsonNode? Value, bool Accepted)[] Cases =
    [
        // A bigint in a row: the digits a JavaScript bigint's toString() writes, in its type's range.
        ("BigRow", "0", true),
        ("BigRow", "-5", true),
        ("BigRow", "9223372036854775807", true),
        ("BigRow", "-9223372036854775808", true),
        ("BigRow", "abc", false),
        ("BigRow", "", false),
        ("BigRow", "01", false),
        ("BigRow", "-0", false),
        ("BigRow", "+5", false),
        ("BigRow", " 5", false),
        ("BigRow", "1.0", false),
        ("BigRow", "9223372036854775808", false),
        ("BigRow", "-9223372036854775809", false),

        // Never a number, which the driver doesn't hand a bigint over as, even one a double holds exactly.
        ("BigRow", 5, false),
        ("BigRow", 0, false),
        ("BigRow", null, false),

        // And in a CHECK's range, inclusive or not, whole or not: > 2.5 is at least 3, < 100 at most 99.
        ("AtLeastZeroRow", "0", true),
        ("AtLeastZeroRow", "9223372036854775807", true),
        ("AtLeastZeroRow", "-1", false),
        ("BetweenRow", "3", true),
        ("BetweenRow", "99", true),
        ("BetweenRow", "2", false),
        ("BetweenRow", "100", false),

        // Dates and times in JSON: seconds always, a fraction of exactly the type's scale or none, an offset only on
        // datetimeoffset, a zero one as Z.
        ("DateRow", "2026-03-04", true),
        ("DateRow", "2026-03-04T00:00:00", false),
        ("Time0Row", "05:06:07", true),
        ("Time0Row", "05:06:07.1", false),
        ("Time3Row", "05:06:07.123", true),
        ("Time3Row", "05:06:07.100", true),
        ("Time3Row", "05:06:07", true),
        ("Time3Row", "05:06", false),
        ("Time3Row", "05:06:07.1", false),
        ("Time3Row", "05:06:07.1234", false),
        ("Datetime20Row", "2026-03-04T05:06:07", true),
        ("Datetime20Row", "2026-03-04T05:06:07.1", false),
        ("Datetime20Row", "2026-03-04T05:06", false),
        ("Datetime23Row", "2026-03-04T05:06:07.123", true),
        ("Datetime23Row", "2026-03-04T05:06:07.100", true),
        ("Datetime23Row", "2026-03-04T05:06:07", true),
        ("Datetime23Row", "2026-03-04T05:06:07.1", false),
        ("Datetime23Row", "2026-03-04T05:06:07Z", false),
        ("Datetime23Row", "2026-03-04T05:06", false),
        ("Datetime27Row", "2026-03-04T05:06:07.1000000", true),
        ("Datetime27Row", "9999-12-31T23:59:59.9999999", true),
        ("Datetime27Row", "2026-03-04T05:06:07", true),
        ("Datetime27Row", "2026-03-04T05:06:07.123", false),
        ("Offset0Row", "2026-03-04T05:06:07+05:30", true),
        ("Offset0Row", "2026-03-04T05:06:07Z", true),
        ("Offset0Row", "0001-01-01T00:00:00-14:00", true),
        ("Offset0Row", "2026-03-04T05:06:07", false),
        ("Offset0Row", "2026-03-04T05:06:07.5Z", false),
        ("Offset3Row", "2026-03-04T05:06:07.500-08:00", true),
        ("Offset3Row", "2026-03-04T05:06:07.1234567Z", false),
        ("Offset3Row", "2026-03-04T05:06:07.5-08:00", false),
        ("DatetimeRow", "2026-03-04T05:06:07.123", true),
        ("DatetimeRow", "2026-03-04T05:06:07.100", true),
        ("DatetimeRow", "2026-03-04T05:06:07.997", true),
        ("DatetimeRow", "2026-03-04T05:06:07", true),
        ("DatetimeRow", "1753-01-01T00:00:00", true),
        ("DatetimeRow", "2026-03-04T05:06:07.12", false),
        ("DatetimeRow", "2026-03-04T05:06:07.124", false),
        ("DatetimeRow", "2026-03-04T05:06:07.1", false),
        ("SmallRow", "2026-03-04T05:06:00", true),
        ("SmallRow", "2026-03-04T05:06:07", false),
        ("SmallRow", "2026-03-04T05:06:00.000", false),

        // A declared result's date has no SQL type and was written by whatever wrote its JSON: the kind's ISO form, unchanged.
        ("DeclaredRow", "2026-03-04T05:06:07.1", true),
    ];

    [Fact]
    public void Each_schema_accepts_what_the_server_and_the_driver_hand_over_and_refuses_the_forms_they_never_do() =>
        Verdicts(
            "module.ts",
            ZodEmitter.Emit(Document()).Expected(),
            new JsonArray([.. Cases.Select(c => Case("schema", ZodModule.SchemaOf(c.Type), c.Value, c.Accepted))]));

    /// <summary>
    /// The JSON Schema document holds a date or time in JSON to the same forms, validated by Ajv. A rowset's <c>bigint</c> is
    /// described there as JSON writes it, an integer, so only the cases of values in JSON are its.
    /// </summary>
    [Fact]
    public void Each_json_schema_definition_accepts_what_the_server_writes_and_refuses_the_forms_it_never_does() =>
        Verdicts(
            "document.json",
            JsonSchemaEmitter.Emit(Document()).Expected(),
            new JsonArray([.. Cases.Where(c => Types[c.Type].Encoding == IrEncoding.Json).Select(c => Case("definition", c.Type, c.Value, c.Accepted))]));

    /// <summary>A procedure for each type, returning rows of it, so that each type has a schema of its own under its name.</summary>
    private static IrDocument Document() => new(
        new IrSource("tsql"),
        [.. Types.Select(t => new IrObject($"s.{t.Key}", t.Key[..^"Row".Length], IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(t.Value.Encoding, new IrArray(new IrRef(t.Key)), [])]))],
        Types.ToDictionary(t => t.Key, t => new IrObjectType([new IrField("v", t.Value.Scalar, IrPresence.Always, [])])),
        []);

    private static JsonObject Case(string kind, string name, JsonNode? value, bool accepted) => new()
    {
        [kind] = name,
        ["value"] = new JsonObject { ["v"] = value?.DeepClone() },
        ["accepted"] = accepted,
    };

    /// <summary>Runs <c>verdicts.mjs</c> on <paramref name="cases"/> against <paramref name="text"/>, written as <paramref name="file"/>.</summary>
    private static void Verdicts(string file, string text, JsonArray cases)
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the schemas' verdicts were not observed");
        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);

        // Under accept/, which git ignores, so that the module's import of zod resolves to the package's own.
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, file), text);
            File.WriteAllText(Path.Combine(folder, "cases.json"), cases.ToJsonString());
            Assert.True(
                EmitCheckPackage.TryRun("node", [Path.Combine("src", "verdicts.mjs"), folder], RepositoryPaths.EmitCheck, out var output, out var why),
                $"A schema decided other than the server and the driver require ({why}):\n{output}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
