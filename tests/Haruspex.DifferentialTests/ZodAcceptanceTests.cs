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
/// <para>
/// Each output goes through two modules: the default one, whose objects strip keys they don't know, and the same IR emitted
/// with <see cref="ZodObjects.Strict"/>. Strict accepts only what the default accepts and refuses a key it doesn't list, so a
/// key the schema names wrongly fails here, even inside JSON text, which the default module's check of dropped keys can't
/// see into (<c>accept.mjs</c>). <see cref="A_key_named_wrongly_fails_the_strict_zod"/> shows it does.
/// </para>
/// </remarks>
public class ZodAcceptanceTests(SqlServerFixture server)
{
    public static TheoryData<string> Fixtures { get; } = [.. Runs.Fixtures];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task The_generated_zod_accepts_every_output(string fixture)
    {
        var (accepted, output, why) = await Accept(fixture, Fixture.Named(fixture).Ir);
        Assert.True(accepted, $"The generated Zod rejected real output ({why}):\n{output}");
    }

    /// <summary>
    /// A plant: every key <c>FOR JSON</c> leaves out when null, named wrongly. The fixture's runs write such a key under its real
    /// name inside JSON text, a rowset column, where the default module's check of dropped keys can't see; the strict module
    /// refuses it, and is the only one that does.
    /// </summary>
    [Fact]
    public async Task A_key_named_wrongly_fails_the_strict_zod()
    {
        var document = Fixture.Named(Planted).Ir;
        var misnamed = document with
        {
            Types = document.Types.ToDictionary(
                t => t.Key,
                t => t.Value with { Fields = [.. t.Value.Fields.Select(f => f.Presence == IrPresence.OmittedWhenNull ? f with { Name = f.Name + "_" } : f)] },
                StringComparer.Ordinal),
        };
        Assert.NotEqual(document.Types, misnamed.Types);

        var (accepted, output, _) = await Accept(Planted, misnamed);
        Assert.False(accepted, "The strict Zod accepted output whose keys it names wrongly.");
        var rejections = output.Split('\n').Where(l => l.Contains(" rejected the output", StringComparison.Ordinal) || l.Contains(" dropped ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(rejections);
        Assert.All(rejections, l => Assert.Contains("(strict) rejected the output", l, StringComparison.Ordinal));
    }

    /// <summary>
    /// The fixture the plant is made in: <c>meter.meter_card</c> returns JSON text holding a key left out when null. Planted in
    /// every fixture (16 Nov 2026), the strict module refused more runs than the default one in six, and in this one the
    /// default refused none.
    /// </summary>
    private const string Planted = "meters";

    /// <summary>Runs every output of a fixture's runs through the Zod emitted for <paramref name="document"/>, strip and strict.</summary>
    private async Task<(bool Accepted, string Output, string? Why)> Accept(string fixture, IrDocument document)
    {
        server.Require();
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the generated Zod was not run against real output");

        var folder = Path.Combine(RepositoryPaths.EmitCheck, "accept", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"{fixture}.zod.ts"), ZodEmitter.Emit(document).Expected(), TestContext.Current.CancellationToken);
            var strict = ZodEmitter.Emit(document, new ZodOptions { Objects = ZodObjects.Strict }).Expected();
            await File.WriteAllTextAsync(Path.Combine(folder, $"{fixture}.strict.zod.ts"), strict, TestContext.Current.CancellationToken);
            using (var cases = File.Create(Path.Combine(folder, "cases.json")))
            using (var json = new Utf8JsonWriter(cases))
            {
                json.WriteStartObject();
                json.WriteString("module", $"{fixture}.zod.ts");
                json.WriteString("strictModule", $"{fixture}.strict.zod.ts");
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
            var accepted = EmitCheckPackage.TryRun("node", [Path.Combine("src", "accept.mjs"), folder], RepositoryPaths.EmitCheck, out var output, out var why);
            return (accepted, output, why);
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
            // No row reads as null, whatever the result's type: the reader convention in the IR's README, and what the mssql
            // driver hands over (DriverJsonObservationTests).
            if (set.Rows.Count == 0)
            {
                json.WriteNullValue();
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
