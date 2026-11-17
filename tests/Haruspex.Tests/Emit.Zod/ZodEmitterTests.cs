using System.Text.RegularExpressions;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>Emitter behaviour on hand-built IR, independent of any front end.</summary>
public class ZodEmitterTests
{
    private static IrDocument OneResult(IrEncoding encoding, params IrField[] fields) => new(
        new IrSource("tsql"),
        [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(encoding, new IrArray(new IrRef("PRow")), [])])],
        new Dictionary<string, IrObjectType> { ["PRow"] = new([.. fields]) },
        []);

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    [Fact]
    public void Quotes_field_names_that_are_not_identifiers()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("first name", new IrScalar(IrScalarKind.String, null, "nvarchar(10)")))).Expected();

        Assert.Contains("  \"first name\": z.string(),\n", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IrEncoding.Json, IrScalarKind.Number, IrFormat.Int64, "bigint", "z.number()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Number, IrFormat.Int64, "bigint", "z.string()")]
    // Without facets an integer format still states its range: the IR's format is a claim about the values.
    [InlineData(IrEncoding.Json, IrScalarKind.Number, IrFormat.Int32, "int", "z.int32()")]
    [InlineData(IrEncoding.Json, IrScalarKind.Number, IrFormat.Int16, "smallint", "z.int().min(-32768).max(32767)")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Number, IrFormat.Uint8, "tinyint", "z.int().min(0).max(255)")]
    [InlineData(IrEncoding.Json, IrScalarKind.Number, IrFormat.Decimal, "decimal(19,2)", "z.number()")]
    [InlineData(IrEncoding.Json, IrScalarKind.String, null, "nvarchar(10)", "z.string()")]
    // In JSON, dates, times and GUIDs are strings in a known format, validated but not converted.
    [InlineData(IrEncoding.Json, IrScalarKind.Date, null, "date", "z.iso.date()")]
    [InlineData(IrEncoding.Json, IrScalarKind.Time, null, "time(0)", "z.iso.time()")]
    [InlineData(IrEncoding.Json, IrScalarKind.Datetime, IrFormat.Local, "datetime2(0)", "z.iso.datetime({ local: true })")]
    [InlineData(IrEncoding.Json, IrScalarKind.Datetime, IrFormat.Local, "smalldatetime", "z.iso.datetime({ local: true })")]
    [InlineData(IrEncoding.Json, IrScalarKind.Datetime, IrFormat.Offset, "datetimeoffset", "z.iso.datetime({ offset: true })")]
    [InlineData(IrEncoding.Json, IrScalarKind.Uuid, null, "uniqueidentifier", "z.guid()")]
    // In a rowset the mssql driver returns them as Date objects.
    [InlineData(IrEncoding.Rowset, IrScalarKind.Date, null, "date", "z.date()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Time, null, "time(0)", "z.date()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Datetime, IrFormat.Local, "datetime2", "z.date()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Datetime, IrFormat.Offset, "datetimeoffset", "z.date()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Uuid, null, "uniqueidentifier", "z.guid()")]
    [InlineData(IrEncoding.Rowset, IrScalarKind.Boolean, null, "bit", "z.boolean()")]
    public void Maps_each_scalar_as_its_encoding_delivers_it(IrEncoding encoding, IrScalarKind kind, IrFormat? format, string sql, string zod)
    {
        var ts = ZodEmitter.Emit(OneResult(encoding, Field("v", new IrScalar(kind, format, sql)))).Expected();

        // An optional trailing comment may explain the mapping (int64 precision, say).
        Assert.Matches($@"\n  v: {Regex.Escape(zod)}( /\*[^\n]*\*/)?,\n", ts);
    }

    [Fact]
    public void Separates_absent_keys_from_null_values()
    {
        var because = new[] { new IrNullReason(IrNullRule.NullableColumn, "s.t.c") };
        var text = new IrScalar(IrScalarKind.String, null, "nvarchar(10)");
        var ts = ZodEmitter.Emit(OneResult(
            IrEncoding.Json,
            new IrField("omitted", text, IrPresence.OmittedWhenNull, [.. because]),
            new IrField("kept", text, IrPresence.Always, [.. because]))).Expected();

        Assert.Contains("  omitted: z.string().optional(),\n", ts, StringComparison.Ordinal);
        Assert.Contains("  kept: z.string().nullable(),\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_text_stays_a_plain_string_even_in_a_json_result()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("v", new IrJsonText()))).Expected();

        Assert.Contains("  /** A string holding JSON. */\n  v: z.string(),\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_an_unknown_with_its_reason_and_never_a_guess()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Rowset, Field("v", new IrUnknownType(IrUnknownReason.UnsupportedExpression)))).Expected();

        Assert.Contains("  /** Not known (unsupported-expression); the IR's unknowns say why. */\n  v: z.unknown(),\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Emits_a_procedure_with_unknown_results_as_unknown()
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], null)],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("s.p", "results", IrUnknownReason.UnsupportedStatement, "IF", "p.sql:3:1")]);

        var ts = ZodEmitter.Emit(doc).Expected();

        Assert.Contains("export const PSchema = z.unknown()", ts, StringComparison.Ordinal);
        Assert.Contains("export type P = z.infer<typeof PSchema>", ts, StringComparison.Ordinal);
    }

    private static IrDocument Absent(IrType type) => new(
        new IrSource("tsql"),
        [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, type, [new IrNullReason(IrNullRule.NoRows, "p.sql:2:1")])])],
        new Dictionary<string, IrObjectType> { ["PRow"] = new([Field("v", new IrScalar(IrScalarKind.String, null, "nvarchar(10)"))]) },
        []);

    [Fact]
    public void An_absent_object_result_reads_as_null()
    {
        var ts = ZodEmitter.Emit(Absent(new IrRef("PRow"))).Expected();

        Assert.Contains("export const PSchema = PRowSchema.nullable()\n", ts, StringComparison.Ordinal);
        Assert.Contains("null when no rows match", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_array_result_reads_as_null_too()
    {
        var ts = ZodEmitter.Emit(Absent(new IrArray(new IrRef("PRow")))).Expected();

        Assert.Contains("export const PSchema = z.array(PRowSchema).min(1, { error: \"Expected at least one row\" }).nullable()\n", ts, StringComparison.Ordinal);
        Assert.Contains("null when no rows match", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, 2, ".length(2, { error: \"Expected exactly 2 rows\" })")]
    [InlineData(2, 5, ".min(2, { error: \"Expected at least 2 rows\" }).max(5, { error: \"Expected at most 5 rows\" })")]
    [InlineData(null, 0, ".max(0, { error: \"Expected no rows\" })")]
    public void A_result_s_bounds_on_its_rows_say_what_was_expected_in_rows(int? fewest, int most, string bounds)
    {
        // Zod's own words count an array's items ("expected array to have exactly 1 items"); a result's array is its rows.
        var document = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("PRow")) { MinItems = fewest, MaxItems = most }, [])])],
            new Dictionary<string, IrObjectType> { ["PRow"] = new([Field("v", new IrScalar(IrScalarKind.String, null, "nvarchar(10)"))]) },
            []);

        Assert.Contains("export const PSchema = z.array(PRowSchema)" + bounds + "\n", ZodEmitter.Emit(document).Expected(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dbo.x*/ export const injected = 1; /*")]
    [InlineData("dbo.x\nexport const injected = 1")]
    [InlineData("dbo.x\u2028export const injected = 1")]
    public void Comments_cannot_be_closed_or_broken_by_names_from_the_ir(string id)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject(id, "P", IrObjectKind.Procedure, "p.sql:1:1", [], null)],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown(id, "results", IrUnknownReason.UnsupportedStatement, "d", "p.sql:1:1")]);

        var ts = ZodEmitter.Emit(doc).Expected();

        Assert.DoesNotContain("*/ export const injected", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("\nexport const injected", ts, StringComparison.Ordinal);
        Assert.DoesNotContain('\u2028', ts);
        Assert.Single(ts.Split('\n'), line => line.StartsWith("/**", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Not An Identifier")]
    [InlineData("x = 1; export const y")]
    [InlineData("")]
    public void Rejects_a_name_that_is_not_an_identifier(string name)
    {
        var doc = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("s.p", name, IrObjectKind.Procedure, "p.sql:1:1", [], null)],
            new Dictionary<string, IrObjectType>(),
            []);

        Assert.IsType<Failed<string>>(ZodEmitter.Emit(doc));
    }

    [Theory]
    [InlineData("$money")]
    [InlineData("a$b")]
    public void Accepts_a_name_with_a_dollar(string name)
    {
        var doc = new IrDocument(new IrSource("tsql"), [new IrObject("s.p", name, IrObjectKind.Procedure, "p.sql:1:1", [], null)], new Dictionary<string, IrObjectType>(), []);

        Assert.Contains($"export const {name}Schema = z.unknown()\n", ZodEmitter.Emit(doc).Expected(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_reserved_word_as_a_name()
    {
        var doc = new IrDocument(new IrSource("tsql"), [new IrObject("s.p", "string", IrObjectKind.Procedure, "p.sql:1:1", [], null)], new Dictionary<string, IrObjectType>(), []);

        Assert.Equal("haruspex: error HAR5001: The IR name 'string' is not a valid TypeScript identifier.", Refused(doc));
    }

    [Fact]
    public void Objects_strip_unknown_keys_by_default()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("v", new IrScalar(IrScalarKind.Boolean, null, "bit")))).Expected();

        Assert.Contains("export const PRowSchema = z.object({\n", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("strictObject", ts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IrEncoding.Json)]
    [InlineData(IrEncoding.Rowset)]
    public void Strict_objects_reject_unknown_keys_in_every_result(IrEncoding encoding)
    {
        var ts = ZodEmitter.Emit(OneResult(encoding, Field("v", new IrScalar(IrScalarKind.Boolean, null, "bit"))), new ZodOptions { Objects = ZodObjects.Strict }).Expected();

        Assert.Contains("export const PRowSchema = z.strictObject({\n", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("z.object(", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void Imports_each_module_once_with_its_names_sorted()
    {
        var ts = ZodEmitter.Emit(OneResult(
            IrEncoding.Json,
            Field("b", new IrImport("BSchema", "@app/types")),
            Field("a", new IrArray(new IrImport("ASchema", "@app/types"))),
            Field("c", new IrImport("CSchema", "./c")),
            Field("again", new IrImport("BSchema", "@app/types")))).Expected();

        Assert.Contains("import { z } from 'zod'\nimport { CSchema } from './c'\nimport { ASchema, BSchema } from '@app/types'\n", ts, StringComparison.Ordinal);
        Assert.Contains("  b: BSchema,\n  a: z.array(ASchema),\n  c: CSchema,\n", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_imported_name_from_two_modules_is_refused()
    {
        var doc = OneResult(IrEncoding.Json, Field("a", new IrImport("TagSchema", "./a")), Field("b", new IrImport("TagSchema", "./b")));

        Assert.Equal("haruspex: error HAR5003: 'TagSchema' is imported from both './a' and './b'.", Refused(doc));
    }

    [Theory]
    [InlineData("PRowSchema")]
    [InlineData("PRow")]
    [InlineData("z")]
    [InlineData("jsonText")]
    public void An_imported_name_the_module_defines_is_refused(string name)
    {
        var doc = OneResult(IrEncoding.Json, Field("a", new IrImport(name, "./a")));

        Assert.Equal($"haruspex: error HAR5004: The imported name '{name}' is also a name this module defines or uses.", Refused(doc));
    }

    [Fact]
    public void An_import_specifier_is_quoted_safely()
    {
        var ts = ZodEmitter.Emit(OneResult(IrEncoding.Json, Field("a", new IrImport("ASchema", "./it's")))).Expected();

        Assert.Contains("import { ASchema } from './it\\'s'\n", ts, StringComparison.Ordinal);
    }

    /// <summary>What the emitter refused, written as the CLI writes it.</summary>
    private static string Refused(IrDocument document) => Assert.Single(Assert.IsType<Failed<string>>(ZodEmitter.Emit(document)).Diagnostics).ToString();
}
