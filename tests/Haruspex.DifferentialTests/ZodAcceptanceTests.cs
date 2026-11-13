using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Emit.Zod;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated Zod itself meets real data: every run's output goes through the schema emitted for its procedure,
/// in Node, against the pinned zod. The IR validator checks the IR's claims; this checks that the emitter turned
/// them into schemas that accept what SQL Server really returns, which a wrong mapping (a bound past a rounded
/// double, a length counted in the wrong unit) would not.
/// </summary>
/// <remarks>
/// Rowset values are handed over as the mssql (tedious) driver delivers them, which the Zod targets: numbers as
/// numbers, bigint as a string, dates and times as Date, uniqueidentifier as a string. They are read here with
/// SqlClient and converted, so a decimal reaches Zod as the nearest double to its exact value, which is what
/// tedious computes for precision up to 15 (the emitter checks bounds only there; see Facets.cs in the emitter).
/// </remarks>
public class ZodAcceptanceTests(SqlServerFixture server)
{
    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task The_generated_zod_accepts_every_output(string fixture)
    {
        server.Require();
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated Zod was not run against real output");

        var document = Fixture.Named(fixture).Ir;
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"{fixture}.zod.ts"), ZodEmitter.Emit(document).Expected(), TestContext.Current.CancellationToken);
            using (var cases = File.Create(Path.Combine(folder, "cases.json")))
            using (var json = new Utf8JsonWriter(cases))
            {
                json.WriteStartObject();
                json.WriteString("module", $"{fixture}.zod.ts");
                json.WriteStartArray("cases");
                foreach (var run in Runs.All.Where(r => r.Fixture == fixture))
                {
                    var procedure = document.Objects.Single(o => o.Id == run.Procedure);
                    if (procedure.Results is not { } results)
                        continue;

                    json.WriteStartObject();
                    json.WriteString("run", run.Name);
                    json.WriteString("schema", procedure.Name + "Schema");
                    json.WritePropertyName("value");
                    var observed = await server.ObserveAsync(run);
                    if (results.Count == 1)
                    {
                        WriteResult(json, results[0], observed.Count > 0 ? observed[0] : null);
                    }
                    else
                    {
                        json.WriteStartArray();
                        for (var i = 0; i < results.Count; i++)
                            WriteResult(json, results[i], i < observed.Count ? observed[i] : null);
                        json.WriteEndArray();
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
            Assert.True(
                EmitCheckPackage.TryRun("node", [Path.Combine("src", "accept.mjs"), folder], RepositoryPaths.EmitCheck, out var output, out var why),
                $"The generated Zod rejected real output ({why}):\n{output}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <param name="set">The result set, or null when it did not arrive (a result that may not run, IR 0.6).</param>
    internal static void WriteResult(Utf8JsonWriter json, IrResult result, ObservedResultSet? set)
    {
        // No result set at all reads as a rowset with no rows, or as a JSON result with no row: the reader convention in
        // the IR's README.
        set ??= new ObservedResultSet(result.Encoding == IrEncoding.Json ? ["json"] : [], []);
        if (result.Encoding == IrEncoding.Json)
        {
            // No row: the reader convention in the IR's README.
            if (set.Rows.Count == 0)
            {
                if (result.Type is IrArray)
                {
                    json.WriteStartArray();
                    json.WriteEndArray();
                }
                else
                {
                    json.WriteNullValue();
                }

                return;
            }

            // SQL Server's text exactly, so its numbers are parsed by JSON.parse as an application's would be.
            var text = new StringBuilder();
            foreach (var row in set.Rows)
                text.Append(row[0] as string);
            json.WriteRawValue(text.ToString());
            return;
        }

        json.WriteStartArray();
        foreach (var row in set.Rows)
        {
            json.WriteStartObject();
            for (var c = 0; c < set.Columns.Count; c++)
            {
                json.WritePropertyName(set.Columns[c]);
                WriteDriverValue(json, row[c]);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A value as the mssql driver would deliver it (see the class remarks).</summary>
    private static void WriteDriverValue(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string s:
                json.WriteStringValue(s);
                break;
            case bool b:
                json.WriteBooleanValue(b);
                break;
            case long l:
                json.WriteStringValue(l.ToString(CultureInfo.InvariantCulture));
                break;
            case byte or short or int or decimal:
                json.WriteRawValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                break;
            case SqlDecimal d:
                json.WriteRawValue(d.ToString());
                break;
            case double d:
                json.WriteNumberValue(d);
                break;
            case float f:
                json.WriteNumberValue((double)f);
                break;
            case Guid g:
                json.WriteStringValue(g.ToString("D").ToUpperInvariant());
                break;
            case DateTime d:
                Date(json, d.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "Z");
                break;
            case DateTimeOffset d:
                Date(json, d.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
                break;
            case TimeSpan t:
                Date(json, "1970-01-01T" + t.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) + "Z");
                break;
            default:
                throw new InvalidOperationException($"No driver mapping for {value.GetType()}.");
        }
    }

    private static void Date(Utf8JsonWriter json, string iso)
    {
        json.WriteStartObject();
        json.WriteString("$date", iso);
        json.WriteEndObject();
    }
}
