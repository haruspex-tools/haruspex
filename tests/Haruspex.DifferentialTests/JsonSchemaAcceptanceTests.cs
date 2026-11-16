using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated JSON Schema meets real data, as the generated Zod does in <see cref="ZodAcceptanceTests"/>: every run's output
/// is validated, in Node with Ajv and its formats asserted, against the definition emitted for its procedure. The differential
/// acceptance test is per backend, so a mapping that is wrong only in JSON Schema (a length counted in the wrong unit, a pattern
/// a value doesn't match) fails here.
/// </summary>
/// <remarks>
/// The document describes every value as SQL Server's <c>FOR JSON</c> writes it, a rowset's rows included
/// (<c>docs/decisions/0051-one-json-schema-document-per-module-every-value-as-json.md</c>). A <c>FOR JSON</c> result is handed over
/// as SQL Server's text, exactly, or null when it is absent. A rowset's rows are read here with SqlClient and written as
/// <c>FOR JSON</c> writes each value, by the column's SQL type, in the forms the SQL Server front end's README lists ("Scalars in
/// JSON"): a decimal's exact digits, a <c>bigint</c> in full, a date as <c>YYYY-MM-DD</c>, a date and time with the fraction of a
/// second left out when it is zero, a zero offset as <c>Z</c>, a GUID in upper case.
/// </remarks>
public class JsonSchemaAcceptanceTests(SqlServerFixture server)
{
    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task The_generated_json_schema_accepts_every_output(string fixture)
    {
        server.Require();
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated JSON Schema was not run against real output");

        var document = Fixture.Named(fixture).Ir;
        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"{fixture}.jsonschema.json"), JsonSchemaEmitter.Emit(document).Expected(), TestContext.Current.CancellationToken);
            using (var cases = File.Create(Path.Combine(folder, "cases.json")))
            using (var json = new Utf8JsonWriter(cases))
            {
                json.WriteStartObject();
                json.WriteString("document", $"{fixture}.jsonschema.json");
                json.WriteStartArray("cases");
                foreach (var run in Runs.All.Where(r => r.Fixture == fixture))
                {
                    var procedure = document.Objects.Single(o => o.Id == run.Procedure);
                    if (procedure.Results is not { } results)
                        continue;

                    json.WriteStartObject();
                    json.WriteString("run", run.Name);
                    json.WriteString("definition", procedure.Name);
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
                EmitCheckPackage.TryRun("node", [Path.Combine("src", "accept-json-schema.mjs"), folder], RepositoryPaths.EmitCheck, out var output, out var why),
                $"The generated JSON Schema rejected real output ({why}):\n{output}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <param name="set">The result set, or null when it did not arrive (a result that may not run, IR 0.6).</param>
    private static void WriteResult(Utf8JsonWriter json, IrResult result, ObservedResultSet? set)
    {
        // No result set at all reads as a rowset with no rows, or as a JSON result with no row: the reader convention in the
        // IR's README, as for the Zod.
        set ??= new ObservedResultSet(result.Encoding == IrEncoding.Json ? ["json"] : [], []);
        if (result.Encoding == IrEncoding.Json)
        {
            if (set.Rows.Count == 0)
            {
                json.WriteNullValue();
                return;
            }

            // SQL Server's text exactly, joined as the driver joins it.
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
                WriteAsForJson(json, row[c], set.ColumnTypes[c]);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>A value as <c>FOR JSON</c> writes it (see the class remarks), by the column's SQL type.</summary>
    private static void WriteAsForJson(Utf8JsonWriter json, object? value, string sqlType)
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
            case byte or short or int or long or decimal:
                json.WriteRawValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                break;
            case SqlDecimal d:
                json.WriteRawValue(d.ToString());
                break;
            case double d:
                json.WriteRawValue(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case float f:
                json.WriteRawValue(f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case Guid g:
                json.WriteStringValue(g.ToString("D").ToUpperInvariant());
                break;
            case DateTime d when sqlType == "date":
                json.WriteStringValue(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            case DateTime d:
                json.WriteStringValue(d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + Fraction(d.TimeOfDay));
                break;
            case DateTimeOffset d:
                var offset = d.Offset == TimeSpan.Zero ? "Z" : d.ToString("zzz", CultureInfo.InvariantCulture);
                json.WriteStringValue(d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + Fraction(d.TimeOfDay) + offset);
                break;
            case TimeSpan t:
                json.WriteStringValue(t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) + Fraction(t));
                break;
            case byte[] bytes:
                json.WriteStringValue(Convert.ToBase64String(bytes));
                break;
            default:
                throw new InvalidOperationException($"No FOR JSON form for {value.GetType()} ({sqlType}).");
        }
    }

    /// <summary>The fraction of a second, its trailing zeros dropped, and nothing when it is zero, as FOR JSON leaves it out.</summary>
    private static string Fraction(TimeSpan time)
    {
        var ticks = time.Ticks % TimeSpan.TicksPerSecond;
        return ticks == 0 ? "" : "." + ticks.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0');
    }
}
