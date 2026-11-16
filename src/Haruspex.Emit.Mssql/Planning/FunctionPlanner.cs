using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Emit.Mssql.Planning;

/// <summary>
/// The first stage: decides each procedure's function (its name, what it sends and how it reads its results), and refuses a
/// document whose functions can't be named before anything is written.
/// </summary>
internal static class FunctionPlanner
{
    /// <summary>
    /// The names the module binds besides its functions and what it imports, which no function may take. The printer writes them
    /// (<c>MssqlPrinter</c>); a test holds the two lists together.
    /// </summary>
    public static IReadOnlyList<string> ModuleNames { get; } = ["sql", "types", "Queryable", "Issue", "ParameterError", "ResultError", "Checks", "checked", "where", "forJson", "readJson", "readRows"];

    /// <summary>Every procedure's function, by id; or every refusal.</summary>
    public static Outcome<IReadOnlyList<MssqlFunction>> Plan(IrDocument document, MssqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        var procedures = document.Objects.Where(o => o.Kind == IrObjectKind.Procedure).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
        var imported = procedures.SelectMany(Imports).ToHashSet(StringComparer.Ordinal);
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        var refusals = new List<Diagnostic>();
        var functions = new List<MssqlFunction>();
        foreach (var procedure in procedures)
        {
            var name = FunctionName(procedure.Name);
            var why = options.IsReserved(name) ? "is a reserved word"
                : ModuleNames.Contains(name, StringComparer.Ordinal) ? "is a name the module uses for itself"
                : imported.Contains(name) ? "is a name the module imports"
                : bound.TryGetValue(name, out var other) ? $"is the function of {other} too"
                : null;
            if (why is not null)
            {
                refusals.Add(MssqlCodes.FunctionNameTaken.At(null, $"The function of {procedure.Id} would be named {name}, which {why}; rename the procedure in the config file."));
                continue;
            }

            bound[name] = procedure.Id;
            functions.Add(new MssqlFunction(
                name,
                procedure,
                [.. procedure.Parameters.Select(Bind)],
                procedure.Results is { } results ? [.. results.Select(r => r.Encoding)] : null));
        }

        return Outcome.Of<IReadOnlyList<MssqlFunction>>(functions, refusals);
    }

    /// <summary>
    /// The function's name: the procedure's settled name with its first word in lower case, as TypeScript names a value whose
    /// type has the same name (<c>GetOrder</c> is <c>getOrder</c>, <c>URLParts</c> <c>urlParts</c>). It is the one name the
    /// emitter makes; the config's naming settled the procedure's.
    /// </summary>
    public static string FunctionName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var upper = 0;
        while (upper < name.Length && char.IsAsciiLetterUpper(name[upper]))
            upper++;

        // A run of capitals before a lower-case letter keeps its last one, which starts the next word.
        var lower = upper > 1 && upper < name.Length && char.IsAsciiLetterLower(name[upper]) ? upper - 1 : upper;
        return string.Concat(name[..lower].Select(char.ToLowerInvariant)) + name[lower..];
    }

    /// <summary>
    /// The schemas the module imports by name for <paramref name="procedure"/>, which no function may take. Its types come through
    /// one namespace (<c>types.GetOrder</c>), so that a function never clashes with the type of its own results, which under
    /// camel-case naming has its name.
    /// </summary>
    private static IEnumerable<string> Imports(IrObject procedure)
    {
        yield return SchemaModuleNames.ResultsSchema(procedure.Name);
        if (procedure.Parameters.Count > 0)
            yield return SchemaModuleNames.ParametersSchema(procedure.Name);
        if (procedure.Parameters.Any(p => p.IsOutput))
            yield return SchemaModuleNames.OutputSchema(procedure.Name);
    }

    /// <summary>
    /// How a parameter is sent. One that may be left out (a default, or an <c>OUTPUT</c> one with a default) is bound only when
    /// given, since bound as undefined it is sent NULL. An <c>OUTPUT</c> one without a default is always bound: unbound, SQL
    /// Server refuses the call (error 201), so NULL is sent when the caller leaves it out.
    /// </summary>
    private static Binding Bind(IrParameter parameter) =>
        new(SchemaModuleNames.Key(parameter.Name), DriverTypes.Of(parameter.Type), parameter.IsOutput, OnlyWhenGiven: parameter.HasDefault);
}
