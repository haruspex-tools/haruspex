using System.Data;
using System.Globalization;
using System.Text.Json;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A fixture's <c>runs.json</c>: the calls the differential tests make, of its procedures and its query files, the procedures
/// they leave out with the reason, and whether the seed must reach every edge. The keys are described in <c>tests/fixtures/README.md</c>.
/// </summary>
/// <param name="SeedReachesEveryEdge">Whether the seed must reach every bound, full length and value of a set the IR claims.</param>
/// <param name="Runs">The calls, in the order the file lists them.</param>
/// <param name="NotRun">Procedures deliberately not run, each with the reason.</param>
/// <param name="QueryRuns">The calls of the fixture's query files, in the order the file lists them among its runs.</param>
internal sealed record RunFile(bool SeedReachesEveryEdge, IReadOnlyList<ProcedureRun> Runs, IReadOnlyDictionary<string, string> NotRun, IReadOnlyList<QueryRun> QueryRuns)
{
    // The file allows comments and trailing commas, as haruspex.json and tsconfig.json do, so a run can say why it is there.
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Reads a deployed fixture's runs, failing on a key, setup, procedure or parameter it doesn't know.</summary>
    public static RunFile Read(Fixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var path = fixture.Runs ?? throw new ArgumentException($"The fixture {fixture.Name} is not deployed, so it has no runs.", nameof(fixture));
        using var document = JsonDocument.Parse(File.ReadAllText(path), Options);
        var root = document.RootElement;
        Known(root, path, "seedReachesEveryEdge", "setups", "runs", "notRun");

        var setups = root.TryGetProperty("setups", out var s)
            ? s.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : [];
        var model = SqlScriptFolder.Read(fixture.Database).Model;

        var runs = new List<ProcedureRun>();
        var queryRuns = new List<QueryRun>();
        foreach (var run in root.GetProperty("runs").EnumerateArray())
        {
            if (run.TryGetProperty("query", out _))
            {
                queryRuns.Add(ReadQueryRun(fixture, run, path));
                continue;
            }

            Known(run, path, "name", "procedure", "parameters", "setup");
            var name = run.GetProperty("name").GetString()!;
            var procedure = run.GetProperty("procedure").GetString()!;
            var declared = Declared(model, procedure, path);
            var parameters = new Dictionary<string, RunArgument>(StringComparer.Ordinal);
            if (run.TryGetProperty("parameters", out var given))
            {
                foreach (var parameter in given.EnumerateObject())
                {
                    var type = declared.GetValueOrDefault(parameter.Name)
                        ?? throw new InvalidOperationException($"{path}: {procedure} has no parameter {parameter.Name}.");
                    parameters[parameter.Name] = Value(parameter.Value, type, $"{path}: {name}, {parameter.Name}");
                }
            }

            string? setup = null;
            if (run.TryGetProperty("setup", out var named))
            {
                setup = setups.GetValueOrDefault(named.GetString()!)
                    ?? throw new InvalidOperationException($"{path}: {name} names the setup \"{named.GetString()}\", which \"setups\" doesn't define.");
            }

            // The fixture's name leads, so a run's name is unique across fixtures and says where it comes from.
            runs.Add(new ProcedureRun($"{fixture.Name}: {name}", fixture.Name, procedure, parameters, setup));
        }

        var notRun = root.TryGetProperty("notRun", out var n)
            ? n.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : [];
        var edges = root.TryGetProperty("seedReachesEveryEdge", out var e) && e.GetBoolean();
        return new RunFile(edges, runs, notRun, queryRuns);
    }

    /// <summary>
    /// A run of a query file, which names the file by its path in the fixture's folder and its parameters by the names the file
    /// uses. It has no setup: every run of a query file only reads.
    /// </summary>
    private static QueryRun ReadQueryRun(Fixture fixture, JsonElement run, string path)
    {
        Known(run, path, "name", "query", "parameters");
        var name = run.GetProperty("name").GetString()!;
        var id = run.GetProperty("query").GetString()!;
        var query = fixture.QueryIr?.Objects.SingleOrDefault(o => o.Id == id)
            ?? throw new InvalidOperationException($"{path}: {name} names the query file {id}, which the fixture's {Fixture.QueryFolder}/ doesn't hold.");
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (run.TryGetProperty("parameters", out var given))
        {
            foreach (var parameter in given.EnumerateObject())
            {
                if (!query.Parameters.Any(p => p.Name == parameter.Name))
                    throw new InvalidOperationException($"{path}: {id} has no parameter {parameter.Name}.");
                parameters[parameter.Name] = parameter.Value.Clone();
            }
        }

        return new QueryRun($"{fixture.Name}: {name}", fixture.Name, id, parameters);
    }

    private static void Known(JsonElement element, string path, params string[] keys)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!keys.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidOperationException($"{path}: unknown key \"{property.Name}\"; the keys are {string.Join(", ", keys)}.");
        }
    }

    /// <summary>The declared type of each of the procedure's parameters, by name.</summary>
    private static Dictionary<string, SqlType?> Declared(DatabaseModel model, string procedure, string path)
    {
        var parts = procedure.Split('.');
        var found = (parts.Length == 2 ? model.FindProcedure(new ObjectName(parts[0], parts[1])) : null)
            ?? throw new InvalidOperationException($"{path}: the fixture has no procedure {procedure}.");
        return found.Parameters.ToDictionary(p => p.Name, p => p.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The argument with the procedure's declared type, so a run file says <c>"@mode": 1</c> and the call passes a
    /// <c>tinyint</c> because the procedure declares one, and <c>"@day": "0001-01-01"</c> reaches a <c>date</c> parameter as
    /// that day, as an <c>EXEC</c> would (<see cref="RunArgument"/>).
    /// </summary>
    private static RunArgument Value(JsonElement value, SqlType? type, string where)
    {
        // sysname is the built-in alias of nvarchar(128), which is read as a type referred to by name.
        if (type is { Kind: SqlTypeKind.Named, Name: var alias } && alias.Equals("sysname", StringComparison.OrdinalIgnoreCase))
            return new(Read(value, v => v.GetString()), SqlDbType.NVarChar, Length: 128);
        if (type is not { Kind: SqlTypeKind.BuiltIn })
            return Unsupported();

        return type.Name switch
        {
            "tinyint" => new(Read(value, v => v.GetByte()), SqlDbType.TinyInt),
            "smallint" => new(Read(value, v => v.GetInt16()), SqlDbType.SmallInt),
            "int" => new(Read(value, v => v.GetInt32()), SqlDbType.Int),
            "bigint" => new(Read(value, v => v.GetInt64()), SqlDbType.BigInt),
            "bit" => new(Read(value, v => v.GetBoolean()), SqlDbType.Bit),

            // decimal is decimal(18,0), as SQL Server reads it.
            "decimal" or "numeric" => new(Read(value, v => v.GetDecimal()), SqlDbType.Decimal, Precision: Argument(type, 0, 18), Scale: Argument(type, 1, 0)),
            "money" => new(Read(value, v => v.GetDecimal()), SqlDbType.Money),
            "smallmoney" => new(Read(value, v => v.GetDecimal()), SqlDbType.SmallMoney),
            "real" => new(Read(value, v => v.GetSingle()), SqlDbType.Real),

            // float(1) to float(24) is real.
            "float" when Argument(type, 0, 53) <= 24 => new(Read(value, v => v.GetSingle()), SqlDbType.Real),
            "float" => new(Read(value, v => v.GetDouble()), SqlDbType.Float),
            "uniqueidentifier" => new(Read(value, v => Guid.Parse(v.GetString()!, CultureInfo.InvariantCulture)), SqlDbType.UniqueIdentifier),
            "date" => new(Read(value, Date), SqlDbType.Date),
            "datetime" => new(Read(value, Date), SqlDbType.DateTime),
            "smalldatetime" => new(Read(value, Date), SqlDbType.SmallDateTime),
            "datetime2" => new(Read(value, Date), SqlDbType.DateTime2, Scale: Argument(type, 0, 7)),
            "time" => new(Read(value, v => TimeSpan.Parse(v.GetString()!, CultureInfo.InvariantCulture)), SqlDbType.Time, Scale: Argument(type, 0, 7)),
            "datetimeoffset" => new(Read(value, v => DateTimeOffset.Parse(v.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None)), SqlDbType.DateTimeOffset, Scale: Argument(type, 0, 7)),
            "char" => new(Read(value, v => v.GetString()), SqlDbType.Char, Length: Length(type)),
            "varchar" => new(Read(value, v => v.GetString()), SqlDbType.VarChar, Length: Length(type)),
            "nchar" => new(Read(value, v => v.GetString()), SqlDbType.NChar, Length: Length(type)),
            "nvarchar" => new(Read(value, v => v.GetString()), SqlDbType.NVarChar, Length: Length(type)),

            // Written as SQL writes it, 0x and two hex digits a byte.
            "binary" => new(Read(value, Bytes), SqlDbType.Binary, Length: Length(type)),
            "varbinary" => new(Read(value, Bytes), SqlDbType.VarBinary, Length: Length(type)),
            "image" => new(Read(value, Bytes), SqlDbType.Image),

            // Eight bytes, sent as binary(8), which converts to a rowversion parameter (observed on the test server, 25 Nov 2026).
            "timestamp" or "rowversion" => new(Read(value, Bytes), SqlDbType.Binary, Length: 8),
            _ => Unsupported(),
        };

        static object? Read(JsonElement value, Func<JsonElement, object?> read) => value.ValueKind == JsonValueKind.Null ? null : read(value);
        static object Date(JsonElement value) => DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None);
        static object Bytes(JsonElement value) => Convert.FromHexString(value.GetString()![2..]);

        // A type written with no argument takes its default: char(1), datetime2(7), decimal(18,0).
        static byte Argument(SqlType type, int index, byte omitted) =>
            type.Arguments.Count > index ? byte.Parse(type.Arguments[index], CultureInfo.InvariantCulture) : omitted;
        static int Length(SqlType type) => type.Arguments.Count == 0 ? 1
            : type.Arguments[0].Equals("max", StringComparison.OrdinalIgnoreCase) ? -1
            : int.Parse(type.Arguments[0], CultureInfo.InvariantCulture);

        RunArgument Unsupported() => throw new InvalidOperationException($"{where}: no argument of the declared type is supported yet; add the type here.");
    }
}
