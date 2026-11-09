using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>The procedures selected, and every problem with the patterns that selected them.</summary>
/// <param name="Procedures">The selected procedures, in the order given.</param>
/// <param name="Problems">Patterns and schemas that match nothing; selection fails when there are any.</param>
public sealed record SelectionResult(IReadOnlyList<ObjectName> Procedures, IReadOnlyList<ConfigProblem> Problems);

/// <summary>
/// Which procedures are the interface (haruspex-design.md, "Which procedures are the interface", reshaped per schema by
/// Richard on 9 Nov 2026). Each schema's block selects among that schema's procedures by their own names: <c>include</c>,
/// then <c>exclude</c>, which wins. Nothing is selected unless its schema has a block with an include, so typing every
/// procedure is something asked for, never the default.
/// </summary>
/// <remarks>
/// Whatever matches nothing is an error, never ignored: a schema key that names no schema of the project, an include
/// that selects nothing in its schema (a typo would quietly drop an interface from the generated code), and an exclude
/// that removes nothing the includes selected (a stale one would quietly stop excluding the day that name comes back).
/// </remarks>
public static class ProcedureSelection
{
    /// <summary>Selects by the config file's schema blocks.</summary>
    public static SelectionResult Select(IReadOnlyList<ObjectName> procedures, HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(config);

        var selected = new List<ObjectName>();
        var problems = new List<ConfigProblem>();
        foreach (var schema in config.Schemas)
        {
            var own = procedures.Where(p => string.Equals(p.Schema, schema.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (own.Count == 0)
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, schema.At,
                    $"the project has no procedure in a schema named \"{schema.Name}\". A schema key is the schema's name as it is, without brackets."));
                continue;
            }

            if (schema.Include is not { } include)
                continue;

            foreach (var pattern in include.Where(i => !own.Any(p => i.Pattern.Matches(p.Name))))
                problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, pattern.At, $"'{pattern.Pattern.Text}' matches no procedure in the schema \"{schema.Name}\"."));

            var included = own.Where(p => include.Any(i => i.Pattern.Matches(p.Name))).ToList();
            foreach (var pattern in schema.Exclude.Where(e => !included.Any(p => e.Pattern.Matches(p.Name))))
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, pattern.At,
                    $"'{pattern.Pattern.Text}' excludes nothing: it matches no procedure that \"include\" selects in the schema \"{schema.Name}\". Remove it."));
            }

            selected.AddRange(included.Where(p => !schema.Exclude.Any(e => e.Pattern.Matches(p.Name))));
        }

        return new SelectionResult([.. procedures.Where(selected.Contains)], problems);
    }

    /// <summary>
    /// Selects by command line patterns (<c>schema.pattern</c>), a one-off override of the file's includes. A schema's
    /// exclude still applies, but isn't required to exclude anything: the run may leave out what it is for. A pattern that
    /// matches nothing is reported with no location.
    /// </summary>
    public static (IReadOnlyList<ObjectName> Procedures, IReadOnlyList<ObjectPattern> Unmatched) Select(
        IReadOnlyList<ObjectName> procedures, IReadOnlyList<ObjectPattern> include, HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(config);

        var selected = procedures
            .Where(p => include.Any(i => i.Matches(p)))
            .Where(p => config.Schema(p.Schema) is not { } schema || !schema.Exclude.Any(e => e.Pattern.Matches(p.Name)))
            .ToList();
        return (selected, [.. include.Where(i => !procedures.Any(i.Matches))]);
    }
}
