using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// Which procedures are the interface. In most databases most procedures write or administer, and only some are worth
/// typing. Each schema's block selects among that schema's procedures by their own names: <c>include</c>, then
/// <c>exclude</c>, which wins, because people think "in this schema, these procedures"
/// (docs/decisions/0010-procedures-are-selected-per-schema.md). Nothing is selected unless its schema has a block with an include, so typing every
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
    /// <returns>The selected procedures, in the order given; or, when a pattern or a schema matches nothing, every such problem.</returns>
    public static Outcome<EquatableArray<ObjectName>> Select(IReadOnlyList<ObjectName> procedures, HaruspexConfig config)
    {
        var (selected, problems) = Selected(procedures, config);
        return Outcome.Of(selected, problems);
    }

    /// <summary>
    /// What the config file's schema blocks select, whatever is wrong with their patterns: the procedures whose overrides apply
    /// in a run whose <c>--include</c> selects fewer, which isn't about the file's patterns.
    /// </summary>
    public static EquatableArray<ObjectName> SelectedByFile(IReadOnlyList<ObjectName> procedures, HaruspexConfig config) =>
        Selected(procedures, config).Procedures;

    private static (EquatableArray<ObjectName> Procedures, List<Diagnostic> Problems) Selected(IReadOnlyList<ObjectName> procedures, HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(config);

        var selected = new List<ObjectName>();
        var problems = new List<Diagnostic>();
        foreach (var schema in config.Schemas)
        {
            var own = procedures.Where(p => string.Equals(p.Schema, schema.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (own.Count == 0)
            {
                problems.Add(ConfigCodes.NoSuchSchema.At(schema.At,
                    $"the project has no procedure in a schema named \"{schema.Name}\". A schema key is the schema's name as it is, without brackets."));
                continue;
            }

            if (schema.Include is not { } include)
                continue;

            foreach (var pattern in include.Where(i => !own.Any(p => i.Pattern.Matches(p.Name))))
                problems.Add(ConfigCodes.IncludeMatchesNothing.At(pattern.At, $"'{pattern.Pattern.Text}' matches no procedure in the schema \"{schema.Name}\". Correct it, or remove it."));

            var included = own.Where(p => include.Any(i => i.Pattern.Matches(p.Name))).ToList();
            foreach (var pattern in schema.Exclude.Where(e => !included.Any(p => e.Pattern.Matches(p.Name))))
            {
                problems.Add(ConfigCodes.ExcludeMatchesNothing.At(pattern.At,
                    $"'{pattern.Pattern.Text}' excludes nothing: it matches no procedure that \"include\" selects in the schema \"{schema.Name}\". Remove it."));
            }

            // A block whose exclude removes all its include selected types nothing, which is as silent as a typo.
            var kept = included.Where(p => !schema.Exclude.Any(e => e.Pattern.Matches(p.Name))).ToList();
            if (included.Count > 0 && kept.Count == 0)
            {
                problems.Add(ConfigCodes.SelectsNothing.At(schema.At,
                    $"the schema \"{schema.Name}\" selects nothing: its \"exclude\" removes every procedure its \"include\" selects. Narrow the exclude, or remove the block."));
            }

            selected.AddRange(kept);
        }

        return ([.. procedures.Where(selected.Contains)], problems);
    }

    /// <summary>
    /// Selects by command line patterns (<c>schema.pattern</c>), a one-off override of the file's includes. A schema's
    /// exclude still applies, but isn't required to exclude anything: the run may leave out what it is for. Each problem
    /// is a message for the command line: a pattern that matches no procedure, or one whose every match the file's
    /// excludes leave out (which would otherwise type nothing, silently).
    /// </summary>
    /// <returns>The selected procedures, in the order given; or every pattern that selects nothing.</returns>
    public static Outcome<EquatableArray<ObjectName>> Select(
        IReadOnlyList<ObjectName> procedures, IReadOnlyList<ObjectPattern> include, HaruspexConfig config)
    {
        ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(config);

        bool Excluded(ObjectName p) => config.Schema(p.Schema) is { } schema && schema.Exclude.Any(e => e.Pattern.Matches(p.Name));
        var problems = new List<Diagnostic>();
        foreach (var pattern in include)
        {
            var matches = procedures.Where(pattern.Matches).ToList();
            if (matches.Count == 0)
            {
                problems.Add(ConfigCodes.CommandLineIncludeMatchesNothing.At(null, $"--include '{pattern.Text}' matches no procedure. Correct it, or remove it."));
            }
            else if (matches.All(Excluded))
            {
                var schemas = string.Join(", ", matches.Select(m => config.Schema(m.Schema)?.Name).OfType<string>().Distinct(StringComparer.Ordinal).Select(s => $"schemas.{s}.exclude"));
                problems.Add(ConfigCodes.CommandLineIncludeExcluded.At(null, $"--include '{pattern.Text}' matches only procedures that {schemas} leaves out, so it selects nothing."));
            }
        }

        return Outcome.Of<EquatableArray<ObjectName>>([.. procedures.Where(p => include.Any(i => i.Matches(p)) && !Excluded(p))], problems);
    }
}
