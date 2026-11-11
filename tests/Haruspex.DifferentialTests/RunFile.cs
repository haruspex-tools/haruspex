using System.Globalization;
using System.Text.Json;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.DifferentialTests;

/// <summary>
/// A fixture's <c>runs.json</c>: the calls the differential tests make, the procedures they leave out with the reason, and
/// whether the seed must reach every edge. The keys are described in <c>tests/fixtures/README.md</c>.
/// </summary>
/// <param name="SeedReachesEveryEdge">Whether the seed must reach every bound, full length and value of a set the IR claims.</param>
/// <param name="Runs">The calls, in the order the file lists them.</param>
/// <param name="NotRun">Procedures deliberately not run, each with the reason.</param>
internal sealed record RunFile(bool SeedReachesEveryEdge, IReadOnlyList<ProcedureRun> Runs, IReadOnlyDictionary<string, string> NotRun)
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
        foreach (var run in root.GetProperty("runs").EnumerateArray())
        {
            Known(run, path, "name", "procedure", "parameters", "setup");
            var name = run.GetProperty("name").GetString()!;
            var procedure = run.GetProperty("procedure").GetString()!;
            var declared = Declared(model, procedure, path);
            var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
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
        return new RunFile(edges, runs, notRun);
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
    private static Dictionary<string, DataTypeReference> Declared(DatabaseModel model, string procedure, string path)
    {
        var parts = procedure.Split('.');
        var found = (parts.Length == 2 ? model.FindProcedure(new ObjectName(parts[0], parts[1])) : null)
            ?? throw new InvalidOperationException($"{path}: the fixture has no procedure {procedure}.");
        return found.Body.Parameters.ToDictionary(p => p.VariableName.Value, p => p.DataType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The argument as the CLR type that SqlClient sends as the parameter's declared type, so a run file says
    /// <c>"@mode": 1</c> and the call passes a <c>tinyint</c> because the procedure declares one, as an <c>EXEC</c> would.
    /// </summary>
    private static object? Value(JsonElement value, DataTypeReference type, string where)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        // sysname is the built-in alias of nvarchar(128), which ScriptDom reads as a user type's name.
        if (type is UserDataTypeReference { Name.BaseIdentifier.Value: var alias } && alias.Equals("sysname", StringComparison.OrdinalIgnoreCase))
            return value.GetString();
        return (type as SqlDataTypeReference)?.SqlDataTypeOption switch
        {
            SqlDataTypeOption.TinyInt => value.GetByte(),
            SqlDataTypeOption.SmallInt => value.GetInt16(),
            SqlDataTypeOption.Int => value.GetInt32(),
            SqlDataTypeOption.BigInt => value.GetInt64(),
            SqlDataTypeOption.Bit => value.GetBoolean(),
            SqlDataTypeOption.Date or SqlDataTypeOption.DateTime or SqlDataTypeOption.DateTime2 or SqlDataTypeOption.SmallDateTime =>
                DateTime.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            SqlDataTypeOption.Char or SqlDataTypeOption.VarChar or SqlDataTypeOption.NChar or SqlDataTypeOption.NVarChar => value.GetString(),
            _ => throw new InvalidOperationException($"{where}: no argument of the declared type is supported yet; add the type here."),
        };
    }
}
