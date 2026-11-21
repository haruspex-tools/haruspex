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
    public static IReadOnlyList<string> ModuleNames { get; } = ["sql", "types", "Queryable", "Issue", "ParameterError", "ResultError", "Checks", "checked", "where", "forJson", "readJson", "readRows", "table", "cell"];

    /// <summary>
    /// The names <c>sp_executesql</c> gives its own parameters, which a query file's parameters, passed to it by name beside them,
    /// may not take (compared as the server compares a variable's name, ignoring case).
    /// </summary>
    private static readonly string[] ExecuteSqlOwn = ["@stmt", "@params"];

    /// <summary>Every procedure's and query file's function, by id; or every refusal.</summary>
    public static Outcome<IReadOnlyList<MssqlFunction>> Plan(IrDocument document, MssqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        // Procedures and query files share the module's names (decision 0059's merge of the two): a clash between them is refused.
        var objects = document.Objects.OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
        var imported = objects.SelectMany(Imports).ToHashSet(StringComparer.Ordinal);
        var bound = new Dictionary<string, IrObject>(StringComparer.Ordinal);
        var refusals = new List<Diagnostic>();
        var functions = new List<MssqlFunction>();
        foreach (var obj in objects)
        {
            var name = FunctionName(obj.Name);
            var other = bound.GetValueOrDefault(name);
            var why = options.IsReserved(name) ? "is a reserved word"
                : ModuleNames.Contains(name, StringComparer.Ordinal) ? "is a name the module uses for itself"
                : imported.Contains(name) ? "is a name the module imports"
                : other is not null ? $"is the function of {other.Id} too"
                : null;
            if (why is not null)
            {
                // Only what is involved is renamed: a query file by its file, a procedure in the config file.
                var kinds = new[] { obj, other }.OfType<IrObject>().Select(o => o.Kind).Distinct().ToList();
                var rename = kinds switch
                {
                    [IrObjectKind.Query] => "rename the query file",
                    [IrObjectKind.Procedure] => "rename the procedure in the config file",
                    _ => "rename the procedure in the config file, or the query file",
                };
                refusals.Add(MssqlCodes.FunctionNameTaken.At(null, $"The function of {obj.Id} would be named {name}, which {why}; {rename}."));
                continue;
            }

            bound[name] = obj;

            // The IR allows any identifier but the function's own keys; __proto__ is one an object literal can't hold as a key.
            if (obj.SetNames?.FirstOrDefault(s => s == "__proto__") is { } unusable)
            {
                refusals.Add(MssqlCodes.SetNameUnusable.At(null, $"A result set of {obj.Id} is named {unusable}, which as a key of the object its function returns sets its prototype rather than adding a key; rename the set."));
                continue;
            }

            // The driver keeps a request's parameters as keys of an object, so it never sends one named __proto__ (observed:
            // A_parameter_named_proto_is_never_sent).
            if (obj.Parameters.FirstOrDefault(p => SchemaModuleNames.Key(p.Name) == "__proto__") is { } unsent)
            {
                refusals.Add(MssqlCodes.ParameterUnusable.At(null, $"{obj.Id} has a parameter named {unsent.Name}, which the mssql driver never sends, since it keeps a request's parameters as keys of an object; rename the parameter."));
                continue;
            }

            (string Text, string Declaration)? batch = obj.Kind == IrObjectKind.Query ? Batch(obj, refusals) : null;
            functions.Add(Function(name, obj, document, refusals) with { Batch = batch });
        }

        return Outcome.Of<IReadOnlyList<MssqlFunction>>(functions, refusals);
    }

    /// <summary>
    /// The function of <paramref name="obj"/>, named <paramref name="name"/>: what it binds and how it reads each result set. Its
    /// table-valued parameters' tables come from <paramref name="document"/>; without it, as for the function's shape alone, none
    /// is planned.
    /// </summary>
    public static MssqlFunction Function(string name, IrObject obj, IrDocument? document = null, List<Diagnostic>? refusals = null) =>
        new(name, obj, [.. obj.Parameters.Select(p => Bind(p) with { Table = document is null ? null : Table(obj, p, document, refusals) })], obj.Results is { } results ? [.. results.Select(r => r.Encoding)] : null);

    /// <summary>
    /// The table a table-valued parameter's rows are sent as: its type's name, and each column of its row, in the row's order,
    /// declared as a scalar parameter of its type is (<see cref="DriverTypes"/>); null for any other parameter, and for a
    /// column that can't be declared, which is refused.
    /// </summary>
    private static TableValue? Table(IrObject obj, IrParameter parameter, IrDocument document, List<Diagnostic>? refusals)
    {
        if (parameter is not { TableType: { } key, Type: IrArray { Items: IrRef row } } || !document.TableTypes.TryGetValue(key, out var definition))
            return null;
        var columns = new List<(string, DriverType)>();
        foreach (var field in document.Types[row.Name].Fields)
        {
            if (DriverTypes.Of(field.Type) is not { } type)
            {
                refusals?.Add(MssqlCodes.TableColumnCannotBeDeclared.At(null, $"The function of {obj.Id} must declare the column {field.Name} of the table type {key} to the driver, and its type gives no driver type to declare it as."));
                return null;
            }

            columns.Add((field.Name, type));
        }

        return new TableValue(definition.Schema, definition.Name, [.. columns]);
    }

    /// <summary>
    /// What a query file's function sends <c>sp_executesql</c>: its text, and each parameter declared by its name and its type's
    /// SQL (the type the inference walked the text with, so what was inferred is what runs). A parameter of no type to declare, or
    /// named as <c>sp_executesql</c>'s own, is refused, since the call can't be made.
    /// </summary>
    private static (string Text, string Declaration) Batch(IrObject query, List<Diagnostic> refusals)
    {
        var declarations = new List<string>();
        foreach (var parameter in query.Parameters)
        {
            var sql = parameter.Type is IrScalar { Sql: { } declared } ? declared : null;
            var why = ExecuteSqlOwn.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase) ? "which has a parameter of that name itself; rename it in the file"
                : parameter.Type is IrUnknownType { Reason: var reason } ? $"and its type isn't known ({IrWireNames.Of(reason)}); declare it in the file, as -- @param {parameter.Name} <type>"
                : sql is null ? $"and its type isn't one a declaration can name; declare it in the file as a scalar type, as -- @param {parameter.Name} nvarchar(max)"
                : null;
            if (why is not null)
                refusals.Add(MssqlCodes.QueryParameterCannotBeDeclared.At(null, $"The function of {query.Id} must declare {parameter.Name} to sp_executesql, {why}."));
            else
                declarations.Add(parameter.Name + " " + sql);
        }

        // A query's object holds its text: the reader refuses one without (IrJsonReader).
        return (query.Text ?? throw new ArgumentException($"The query {query.Id} has no text.", nameof(query)), string.Join(", ", declarations));
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
