using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

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
    /// <param name="declaredDocument">
    /// The inferred IR of the procedures of the project whose results are declared but which are not in
    /// <paramref name="document"/>, so that each declaration can be checked against what is inferred. Null when there are none.
    /// </param>
    /// <returns>The document with the config applied and every name settled, with any warnings; or every problem found.</returns>
    public static Outcome<IrDocument> Apply(
        IrDocument document,
        HaruspexConfig config,
        IReadOnlyList<ObjectName> procedures,
        IReadOnlyCollection<ObjectName>? selectedByFile = null,
        string? outputDirectory = null,
        IrDocument? declaredDocument = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(procedures);

        var problems = new List<Diagnostic>();
        var types = document.Types.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var unknowns = document.Unknowns.ToList();
        var claims = new List<IrOverride>();
        var applier = new OverrideApplier(types, unknowns, claims, problems, from => Import(from, config.Directory, outputDirectory));
        var results = document.Objects.Select(o => o.Results).ToArray();
        var procedureNames = new Dictionary<string, (string, JsonLocation?)>(StringComparer.Ordinal);
        var seen = new Dictionary<string, JsonLocation>(StringComparer.Ordinal);

        var declaredHere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in config.Procedures)
        {
            // Results alone may be declared for any procedure: one a selected procedure calls need not be selected, nor in
            // the project (a system procedure, another database's).
            var onlyDeclared = entry is DeclaredResultsOverride { Name: null };
            var target = procedures.Where(p => p.Equals(entry.Procedure)).ToList();
            if (target.Count == 0)
            {
                if (onlyDeclared)
                    Unused(entry, entry.Procedure.ToString());
                else
                    problems.Add(ConfigCodes.NoSuchProcedure.At(entry.At, $"there is no procedure {entry.Key} in the project."));
                continue;
            }

            var index = document.Objects.ToList().FindIndex(o => target.Any(t => t.ToString() == o.Id));
            if (index < 0 && onlyDeclared)
            {
                // Not selected: checked against what is inferred of it, and otherwise there for its callers.
                if (entry is DeclaredResultsOverride { Results: var declared } && declaredDocument?.Objects.FirstOrDefault(o => target.Any(t => t.ToString() == o.Id)) is { Results: { } inferred } checkedObject)
                    Check(entry, declared, checkedObject.Id, inferred, declaredDocument.Types);
                else
                    Unused(entry, target[0].ToString());
                continue;
            }

            if (index < 0)
            {
                if (selectedByFile is null || !selectedByFile.Contains(target[0]))
                {
                    problems.Add(ConfigCodes.NotSelected.At(entry.At,
                        $"the procedure {target[0]} is not selected, so it can't be overridden. Overrides apply only to the procedures its schema's \"include\" selects and \"exclude\" leaves in."));
                }

                continue;
            }

            var obj = document.Objects[index];
            if (seen.TryGetValue(obj.Id, out var earlier))
            {
                problems.Add(ConfigCodes.OverriddenTwice.At(entry.At,
                    $"{obj.Id} already has overrides, at line {earlier.Line}; names are compared ignoring case, as SQL Server compares them. Merge the two."));
                continue;
            }

            seen[obj.Id] = entry.At;
            if (entry.Name is { } name)
                procedureNames[obj.Id] = (name, entry.NameAt);
            switch (entry)
            {
                case DeclaredResultsOverride { Results: var declaration } when results[index] is { } inferred:
                    Check(entry, declaration, obj.Id, inferred, types);
                    break;
                case DeclaredResultsOverride { Results: var declaration }:
                    // The results the project can't show are the declared ones: a claim, listed as one, and still checked at
                    // runtime by the generated validator.
                    results[index] = Declared(declaration, obj.Id, types);
                    unknowns.RemoveAll(u => u.Object == obj.Id && u.Path == "results");
                    claims.Add(new IrOverride(obj.Id, "results", IrOverrideSetting.Results, declaration.Why));
                    declaredHere.Add(obj.Id);
                    break;
                case InferredResultsOverride { Paths: var paths }:
                    foreach (var path in paths)
                        results[index] = applier.Apply(obj, results[index], path);
                    break;
            }
        }

        // A selected procedure whose results are unknowable and not declared: generation goes ahead (a `check`
        // command, which is planned, will fail it), but the statement is named, with what to declare.
        for (var i = 0; i < document.Objects.Count; i++)
        {
            var obj = document.Objects[i];
            if (results[i] is not null || declaredHere.Contains(obj.Id)
                || unknowns.FirstOrDefault(u => u.Object == obj.Id && u.Path == "results" && Unknowable.Contains(u.Reason)) is not { } why)
            {
                continue;
            }

            var file = config.File.Length == 0 ? HaruspexConfig.FileName : Path.GetFileName(config.File);
            problems.Add(ConfigCodes.ResultsToDeclare.At(config.Location,
                $"{obj.Id}: its result sets are unknown: {why.Detail} ({why.At}) Declare them in {file} under procedures[\"{obj.Id}\"].results, as \"none\" or a JSON Schema, or declare the results of the procedure that runs it."));
        }

        // Values a call that isn't followed leaves unknown, under INSERT … EXEC: its results went into a temporary table,
        // which is unknown after it. A declaration can't help there (what a call does to temporary tables is never known
        // from it), but a path override can, so the warning says so. One warning per call, naming it.
        for (var i = 0; i < document.Objects.Count; i++)
        {
            var obj = document.Objects[i];
            if (results[i] is null)
                continue;
            foreach (var call in unknowns.Where(u => u.Object == obj.Id && u.Path != "results" && Unknowable.Contains(u.Reason)).GroupBy(u => u.Detail, StringComparer.Ordinal))
            {
                var paths = call.Select(u => u.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                problems.Add(ConfigCodes.UnknownAfterCall.At(config.Location,
                    $"{obj.Id}: {string.Join(", ", paths)} {(paths.Count == 1 ? "is" : "are")} unknown: {call.Key} A declaration can't type {(paths.Count == 1 ? "it" : "them")}: after a call that is not followed, temporary tables are unknown even when its results are declared. To type the value, override its path under \"paths\" with \"type\" and \"import\"."));
            }
        }

        var settler = new NameSettler(config, types, procedureNames, applier.Names, problems);
        if (problems.Any(p => p.IsError) || settler.Settle(document with { Unknowns = [.. unknowns] }, results) is not { } named)
            return new Failed<IrDocument>([.. problems]);
        return new Succeeded<IrDocument>(named with { Overrides = [.. document.Overrides, .. claims] }, [.. problems]);

        // A declaration of results haruspex infers: an error if they differ, a warning if not (it can go).
        void Check(ProcedureOverride entry, ResultsDeclaration declared, string id, IReadOnlyList<IrResult> inferred, IReadOnlyDictionary<string, IrObjectType> inferredTypes)
        {
            problems.Add(ResultsComparison.Difference(inferred, inferredTypes, declared) is { } difference
                ? ConfigCodes.DeclarationDisagrees.At(entry.At,
                    $"haruspex infers the results of {id}, and the declaration disagrees at {difference} A declaration is for results that can't be inferred; to correct inferred results, override a path under \"paths\".")
                : ConfigCodes.DeclarationAgrees.At(entry.At,
                    $"the declared results agree with the inference, which already infers the results of {id}. Remove the declaration."));
        }

        // A declaration of a procedure that is not selected is there for its callers; with none, it does nothing.
        void Unused(ProcedureOverride entry, string name)
        {
            if (!document.Overrides.Any(o => string.Equals(o.Via, name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(ConfigCodes.DeclarationUnused.At(entry.At,
                    $"the declared results are not used: {name} is not selected, and no selected procedure calls it."));
            }
        }
    }

    /// <summary>
    /// The reasons for results that the project cannot show, which only a declaration can supply. <see cref="Apply"/> reports
    /// every unknown with one of them, with what to declare (<see cref="ConfigCodes.ResultsToDeclare"/>,
    /// <see cref="ConfigCodes.UnknownAfterCall"/>), so nothing else needs to.
    /// </summary>
    public static IReadOnlySet<IrUnknownReason> Declarable => Unknowable;

    private static readonly HashSet<IrUnknownReason> Unknowable =
    [
        IrUnknownReason.DynamicSql,
        IrUnknownReason.ProcedureVariable,
        IrUnknownReason.SystemProcedure,
        IrUnknownReason.MissingProcedure,
        IrUnknownReason.AmbiguousProcedure,
        IrUnknownReason.ExternalCode,
    ];

    /// <summary>
    /// A declaration's results for the procedure <paramref name="id"/>, its types added to <paramref name="types"/> under keys
    /// of that procedure (the declaration's own keys were made from the config file's spelling of its name).
    /// </summary>
    private static EquatableArray<IrResult> Declared(ResultsDeclaration declaration, string id, Dictionary<string, IrObjectType> types)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, type) in declaration.Types)
        {
            var path = type.Identity.Path;
            var candidate = $"{id}:{path}";
            for (var n = 2; types.ContainsKey(candidate) || keys.ContainsValue(candidate); n++)
                candidate = $"{id}:{path}~{n.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            keys[key] = candidate;
        }

        IrType Rekey(IrType type) => IrTypeTree.RenameReferences(type, key => keys[key]);

        foreach (var (key, type) in declaration.Types)
        {
            types[keys[key]] = new IrObjectType([.. type.Fields.Select(f => f with { Type = Rekey(f.Type) })]) { Identity = type.Identity with { Object = id } };
        }

        return [.. declaration.Results.Select(r => r with { Type = Rekey(r.Type) })];
    }

    /// <summary>
    /// A module to import from, in the IR's terms (<see cref="IrImport.From"/>). A relative path (<c>./</c>, <c>../</c>) is
    /// written relative to the config file, like every path in it, and the IR's is relative to the folder the IR is written
    /// to, the output folder, so it is re-based; anything else (a package, a path alias such as <c>@app/types</c>) is kept
    /// as written. This is the config file's path rule, not any language's: how an emitter writes the import is its own.
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
