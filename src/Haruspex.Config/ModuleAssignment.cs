using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// Which module each selected procedure and query file is in, by the config file's <c>split</c>
/// (<c>docs/decisions/0074-a-config-may-split-its-output-into-modules.md</c>): a procedure by its schema, each schema its own
/// module or the module that lists it; a query file by its <c>-- @module</c> comment, or else
/// <see cref="ModuleSplit.QueriesModule"/>. An object in no module, or a module that can't name its files, is refused, so that
/// nothing is left out of the output or written to a file nobody named.
/// </summary>
public static class ModuleAssignment
{
    /// <summary>
    /// Each object's module, by its id in the IR (a procedure's name, a query file's path); empty when the config file doesn't
    /// split its output, where a query file's module comment is a warning, since it does nothing.
    /// </summary>
    public static Outcome<EquatableMap<string, string>> Assign(HaruspexConfig config, IEnumerable<ObjectName> procedures, IEnumerable<QueryFile> queries)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(queries);
        var problems = new List<Diagnostic>();
        if (config.Split is not { } split)
        {
            foreach (var query in queries.Where(q => q.Module is not null))
            {
                problems.Add(ConfigCodes.ModuleUnsplit.At(query.ModuleAt,
                    $"-- @module {query.Module}: the config file doesn't split its output into modules, so the comment does nothing. Add \"split\" to the config file, or remove the comment."));
            }

            return Outcome.Of(EquatableMap.Create<string, string>([]), problems);
        }

        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var procedure in procedures)
        {
            if (ModuleOf(split, procedure.Schema) is { } module)
            {
                assigned[procedure.ToString()] = module;
            }
            else
            {
                problems.Add(ConfigCodes.NoModule.At(split.At,
                    $"the procedure {procedure} is in no module: no module lists its schema, {procedure.Schema}. Add the schema to a module's list, or leave the procedure out of the selection."));
            }
        }

        // A schema that is a module of its own must be able to name its files; a listed module's name was checked as it was read.
        if (split.BySchema)
        {
            foreach (var schema in assigned.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (ModuleNames.Problem(schema) is { } problem)
                    problems.Add(ConfigCodes.BadModuleName.At(split.At, $"the schema '{schema}' can't name a module: {problem} List the modules instead, with a name for its module."));
            }
        }

        // A query file names one of the modules, or the one query files are in by default, which may hold nothing else.
        var modules = (split.Modules?.Select(m => m.Name) ?? assigned.Values).Append(ModuleSplit.QueriesModule).ToHashSet(StringComparer.Ordinal);
        foreach (var query in queries)
        {
            var module = query.Module ?? ModuleSplit.QueriesModule;
            if (modules.Contains(module))
            {
                assigned[query.Path] = module;
            }
            else
            {
                problems.Add(ConfigCodes.NoModule.At(query.ModuleAt,
                    $"-- @module {module}: no module is named {module}; the modules are {Listed([.. modules.Order(StringComparer.Ordinal)])}."));
            }
        }

        // Two modules whose names differ only in case would be one set of files where case is ignored (a listed module's were
        // refused as they were read).
        foreach (var clash in assigned.Values.Distinct(StringComparer.Ordinal).GroupBy(m => m, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add(ConfigCodes.BadModuleName.At(split.At,
                $"the modules {Listed([.. clash.Order(StringComparer.Ordinal)])} differ only in case, and a file system that ignores case would hold one file for both. List the modules instead, naming one of them otherwise."));
        }

        return Outcome.Of(assigned.ToEquatableMap(a => a.Key, a => a.Value), problems);
    }

    /// <summary>
    /// The module of a procedure of <paramref name="schema"/>: the schema itself, or the module whose list holds it, compared
    /// ignoring case as SQL Server compares schema names; null when none does.
    /// </summary>
    private static string? ModuleOf(ModuleSplit split, string schema) =>
        split.Modules is { } modules
            ? modules.FirstOrDefault(m => m.Schemas.Contains(schema, StringComparer.OrdinalIgnoreCase))?.Name
            : schema;

    /// <summary>Items as an English list: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    private static string Listed(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
