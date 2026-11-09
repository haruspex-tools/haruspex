using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>The outcome of applying a config file to a document: the document when there are no errors, and every problem.</summary>
public sealed record ConfigApplied(IrDocument? Document, IReadOnlyList<ConfigProblem> Problems);

/// <summary>
/// Applies a config file to the IR inference produced: the overrides, as transformations of the document, and then the
/// naming rules, which settle every generated name. The result is what is written to <c>haruspex.ir.json</c> and handed to
/// the emitters, which never read the config file themselves.
/// </summary>
public static class ConfigApplication
{
    /// <summary>Applies <paramref name="config"/> to <paramref name="document"/>.</summary>
    /// <param name="document">The inferred IR of the selected procedures, names not yet settled.</param>
    /// <param name="config">The config; <see cref="HaruspexConfig.None"/> when there is no file.</param>
    /// <param name="procedures">Every procedure in the project, to tell an override of a missing procedure from one of an unselected one.</param>
    /// <param name="selectedByFile">
    /// The procedures the file's own schema blocks select. A command line <c>--include</c> may select fewer for one
    /// run; overrides of the procedures it leaves out are skipped, not errors. Null when the selection is the file's.
    /// </param>
    /// <param name="outputDirectory">Where the generated module is written, so that a relative <c>import</c>, written relative to the config file, can be made relative to it. Null to keep imports as written.</param>
    public static ConfigApplied Apply(
        IrDocument document,
        HaruspexConfig config,
        IReadOnlyList<ObjectName> procedures,
        IReadOnlyCollection<ObjectName>? selectedByFile = null,
        string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(procedures);

        var problems = new List<ConfigProblem>();
        var types = document.Types.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var unknowns = document.Unknowns.ToList();
        var claims = new List<IrOverride>();
        var applier = new OverrideApplier(config.File, types, unknowns, claims, problems, from => Import(from, config.Directory, outputDirectory));
        var results = document.Objects.Select(o => o.Results).ToArray();
        var procedureNames = new Dictionary<string, (string, ConfigLocation?)>(StringComparer.Ordinal);
        var seen = new Dictionary<string, ConfigLocation>(StringComparer.Ordinal);

        foreach (var entry in config.Procedures)
        {
            var target = procedures.Where(entry.Procedure.Matches).ToList();
            if (target.Count == 0)
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, entry.At, $"there is no procedure {entry.Procedure.Text} in the project."));
                continue;
            }

            var index = document.Objects.ToList().FindIndex(o => target.Any(t => t.ToString() == o.Id));
            if (index < 0)
            {
                if (selectedByFile is null || !selectedByFile.Contains(target[0]))
                {
                    problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, entry.At,
                        $"the procedure {target[0]} is not selected, so it can't be overridden. Overrides apply only to the procedures its schema's \"include\" selects and \"exclude\" leaves in."));
                }

                continue;
            }

            var obj = document.Objects[index];
            if (seen.TryGetValue(obj.Id, out var earlier))
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Error, config.File, entry.At,
                    $"{obj.Id} already has overrides, at line {earlier.Line}; names are compared ignoring case, as SQL Server compares them. Merge the two."));
                continue;
            }

            seen[obj.Id] = entry.At;
            if (entry.Name is { } name)
                procedureNames[obj.Id] = (name, entry.NameAt);
            foreach (var path in entry.Paths)
                results[index] = applier.Apply(obj, results[index], path);
        }

        var settler = new NameSettler(config.File, config.NamingFor, types, procedureNames, applier.Names, problems);
        var named = problems.Any(p => p.Severity == ConfigSeverity.Error) ? null : settler.Settle(document with { Unknowns = unknowns }, results);
        return new ConfigApplied(named is null ? null : named with { Overrides = claims }, problems);
    }

    /// <summary>
    /// A module to import from, as the generated module must write it. A relative path (<c>./</c>, <c>../</c>) is written
    /// relative to the config file, like every path in it, so it is made relative to the output folder; anything else (a
    /// package, a path alias such as <c>@app/types</c>) is kept as written.
    /// </summary>
    private static string Import(string from, string configDirectory, string? outputDirectory)
    {
        if (outputDirectory is null || !(from.StartsWith("./", StringComparison.Ordinal) || from.StartsWith("../", StringComparison.Ordinal)))
            return from;
        var target = Path.GetFullPath(Path.Join(configDirectory, from));
        var relative = Path.GetRelativePath(Path.GetFullPath(outputDirectory), target).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? relative : "./" + relative;
    }
}
