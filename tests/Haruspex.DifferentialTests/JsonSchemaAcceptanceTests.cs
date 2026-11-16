using System.Data;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Haruspex.DifferentialTests.Server;
using Haruspex.DifferentialTests.Validation;
using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// The generated JSON Schema meets real data, as the generated Zod does in <see cref="ZodAcceptanceTests"/>: every run's output
/// is validated, in Node with Ajv and its formats asserted, against the definition emitted for its procedure. The differential
/// acceptance test is per backend, so a mapping that is wrong only in JSON Schema (a length counted in the wrong unit, a pattern
/// a value doesn't match) fails here.
/// </summary>
/// <remarks>
/// <para>
/// The document describes every value as SQL Server's <c>FOR JSON</c> writes it, a rowset's rows as <c>FOR JSON PATH,
/// INCLUDE_NULL_VALUES</c> writes them (<c>docs/decisions/0051-one-json-schema-document-per-module-every-value-as-json.md</c>), so
/// SQL Server writes them here: a <c>FOR JSON</c> result is the server's text, exactly, or null when it is absent; and each row of a
/// rowset, as the driver read it, is sent back to the server cast to its column's own type and written with <c>FOR JSON PATH,
/// INCLUDE_NULL_VALUES</c>. A test that wrote the forms itself could only show the schema fits its own idea of them.
/// </para>
/// <para>
/// It validates the document with strict objects, which accepts only what the default one does and also refuses a key it doesn't
/// list, so a key the schema names wrongly fails here rather than passing as one more key allowed.
/// </para>
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
            var strict = JsonSchemaEmitter.Emit(document, new JsonSchemaOptions { Objects = JsonSchemaObjects.Strict }).Expected();
            await File.WriteAllTextAsync(Path.Combine(folder, $"{fixture}.jsonschema.json"), strict, TestContext.Current.CancellationToken);
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
                    var written = new List<IReadOnlyList<string>>();
                    foreach (var set in observed)
                        written.Add(await RowsAsSqlServerWritesThem(set));
                    if (results.Count == 1)
                    {
                        WriteResult(json, results[0], observed.Count > 0 ? observed[0] : null, observed.Count > 0 ? written[0] : []);
                    }
                    else
                    {
                        json.WriteStartArray();
                        for (var i = 0; i < results.Count; i++)
                            WriteResult(json, results[i], i < observed.Count ? observed[i] : null, i < observed.Count ? written[i] : []);
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
    /// <param name="rows">Its rows as SQL Server writes them (<see cref="RowsAsSqlServerWritesThem"/>), for a rowset.</param>
    private static void WriteResult(Utf8JsonWriter json, IrResult result, ObservedResultSet? set, IReadOnlyList<string> rows)
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
        foreach (var row in rows)
        {
            // The server wrote each column under its position (c0, c1, …), since a rowset's names may repeat or be empty, which
            // FOR JSON refuses; each value goes back under its column's name, as the server wrote it.
            using var written = JsonDocument.Parse(row);
            json.WriteStartObject();
            for (var c = 0; c < set.Columns.Count; c++)
            {
                json.WritePropertyName(set.Columns[c]);
                json.WriteRawValue(written.RootElement.GetProperty(Position(c)).GetRawText());
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>
    /// Each row of a rowset as SQL Server writes it with <c>FOR JSON PATH, INCLUDE_NULL_VALUES</c>: the row's values sent back as
    /// parameters, each cast to its column's type, so the server writes each in its own form (a <c>time(7)</c>'s seven digits, a
    /// <c>float</c>'s exponent form). Text and binary go back as they are, which writes the same string, so a value is never
    /// converted through a collation's code page; so does a column of the <c>json</c> type, which is text in a rowset. An empty list for a JSON result, which the server wrote already.
    /// </summary>
    private async Task<IReadOnlyList<string>> RowsAsSqlServerWritesThem(ObservedResultSet set)
    {
        if (set.Rows.Count == 0 || set.ColumnTypes.Count == 0)
            return [];
        var select = string.Join(", ", set.ColumnTypes.Select((type, c) => $"{Value(c, type)} AS [{Position(c)}]"));
        var rows = new List<string>();
        await using var session = await server.ReadAsync(TestContext.Current.CancellationToken);
        foreach (var row in set.Rows)
        {
            await using var command = session.Command($"SELECT {select} FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER;");
            for (var c = 0; c < row.Count; c++)
                command.Parameters.Add(Parameter($"@p{c.ToString(CultureInfo.InvariantCulture)}", row[c]));
            var text = new StringBuilder();
            await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                    text.Append(reader.GetString(0));
            }

            rows.Add(text.ToString());
        }

        return rows;
    }

    private static string Position(int column) => "c" + column.ToString(CultureInfo.InvariantCulture);

    /// <summary>The parameter for column <paramref name="column"/>, cast to its type unless it is text or binary.</summary>
    private static string Value(int column, string sqlType)
    {
        var parameter = "@p" + column.ToString(CultureInfo.InvariantCulture);
        var name = sqlType.Split('(')[0];

        // A column of the json type too: FOR JSON would embed it, but in a rowset it is JSON text, a string to parse, which is what
        // every driver hands over and what the IR claims (decision 0051).
        return name is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" or "sysname" or "binary" or "varbinary" or "image" or "json"
            ? parameter
            : $"CAST({parameter} AS {sqlType})";
    }

    /// <summary>
    /// A value read by SqlClient as a parameter that holds it exactly: dates and times at the most precise scale, decimals at their
    /// own precision and scale, floats as doubles, text as <c>nvarchar(max)</c>.
    /// </summary>
    private static SqlParameter Parameter(string name, object? value) => value switch
    {
        null => new SqlParameter(name, SqlDbType.NVarChar, -1) { Value = DBNull.Value },
        string s => new SqlParameter(name, SqlDbType.NVarChar, -1) { Value = s },
        byte[] b => new SqlParameter(name, SqlDbType.VarBinary, -1) { Value = b },
        DateTime d => new SqlParameter(name, SqlDbType.DateTime2) { Scale = 7, Value = d },
        DateTimeOffset d => new SqlParameter(name, SqlDbType.DateTimeOffset) { Scale = 7, Value = d },
        TimeSpan t => new SqlParameter(name, SqlDbType.Time) { Scale = 7, Value = t },
        decimal d => Exact(name, new SqlDecimal(d)),
        SqlDecimal d => Exact(name, d),
        double d => new SqlParameter(name, SqlDbType.Float) { Value = d },
        float f => new SqlParameter(name, SqlDbType.Real) { Value = f },
        _ => new SqlParameter(name, value),
    };

    private static SqlParameter Exact(string name, SqlDecimal value) =>
        new(name, SqlDbType.Decimal) { Precision = value.Precision, Scale = value.Scale, SqlValue = value };
}
