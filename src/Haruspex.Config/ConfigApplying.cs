using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// One application of a config file to one document (<see cref="ConfigApplication.Apply"/>), as named steps over the state
/// they share: each procedure's overrides, then the warnings for what only a declaration or a path override could type, then
/// the names. The state is this run's own, made from the document at the start and never seen outside it.
/// </summary>
internal sealed class ConfigApplying
{
    private readonly IrDocument document;
    private readonly HaruspexConfig config;
    private readonly IReadOnlyList<ObjectName> procedures;
    private readonly IReadOnlyCollection<ObjectName>? selectedByFile;
    private readonly IrDocument? declaredDocument;

    private readonly List<Diagnostic> problems = [];
    private readonly Dictionary<string, IrObjectType> types;
    private readonly List<IrUnknown> unknowns;
    private readonly List<IrOverride> claims = [];
    private readonly OverrideApplier applier;
    private readonly EquatableArray<IrResult>?[] results;
    private readonly Dictionary<string, (string, JsonLocation?)> procedureNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonLocation> seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> declaredHere = new(StringComparer.Ordinal);

    /// <summary>The state of one application; see <see cref="ConfigApplication.Apply"/> for each argument.</summary>
    public ConfigApplying(
        IrDocument document,
        HaruspexConfig config,
        IReadOnlyList<ObjectName> procedures,
        IReadOnlyCollection<ObjectName>? selectedByFile,
        Func<string, string> importFrom,
        IrDocument? declaredDocument)
    {
        this.document = document;
        this.config = config;
        this.procedures = procedures;
        this.selectedByFile = selectedByFile;
        this.declaredDocument = declaredDocument;
        types = document.Types.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        unknowns = [.. document.Unknowns];
        applier = new OverrideApplier(types, unknowns, claims, problems, importFrom);
        results = [.. document.Objects.Select(o => o.Results)];
    }

    /// <summary>Applies the config: the overrides, the warnings, then the names.</summary>
    public Outcome<IrDocument> Apply()
    {
        foreach (var entry in config.Procedures)
            Override(entry);
        WarnOfResultsToDeclare();
        WarnOfValuesUnknownAfterCalls();
        return Named();
    }

    /// <summary>One procedure's overrides: found, selected and given once, then applied.</summary>
    private void Override(ProcedureOverride entry)
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
            return;
        }

        var index = document.Objects.ToList().FindIndex(o => target.Any(t => t.ToString() == o.Id));
        if (index < 0 && onlyDeclared)
        {
            // Not selected: checked against what is inferred of it, and otherwise there for its callers.
            if (entry is DeclaredResultsOverride { Results: var declared } && declaredDocument?.Objects.FirstOrDefault(o => target.Any(t => t.ToString() == o.Id)) is { Results: { } inferred } checkedObject)
                Check(entry, declared, checkedObject.Id, inferred, declaredDocument.Types);
            else
                Unused(entry, target[0].ToString());
            return;
        }

        if (index < 0)
        {
            if (selectedByFile is null || !selectedByFile.Contains(target[0]))
            {
                problems.Add(ConfigCodes.NotSelected.At(entry.At,
                    $"the procedure {target[0]} is not selected, so it can't be overridden. Overrides apply only to the procedures its schema's \"include\" selects and \"exclude\" leaves in."));
            }

            return;
        }

        var obj = document.Objects[index];
        if (seen.TryGetValue(obj.Id, out var earlier))
        {
            problems.Add(ConfigCodes.OverriddenTwice.At(entry.At,
                $"{obj.Id} already has overrides, at line {earlier.Line}; names are compared ignoring case, as SQL Server compares them. Merge the two."));
            return;
        }

        seen[obj.Id] = entry.At;
        if (entry.Name is { } name)
            procedureNames[obj.Id] = (name, entry.NameAt);
        Applied(entry, obj, index);
    }

    /// <summary>A selected procedure's overrides: declared results checked or put in place of unknown ones, or path overrides.</summary>
    private void Applied(ProcedureOverride entry, IrObject obj, int index)
    {
        switch (entry)
        {
            case DeclaredResultsOverride { Results: var declaration } when results[index] is { } inferred:
                Check(entry, declaration, obj.Id, inferred, types);
                break;
            case DeclaredResultsOverride { Results: var declaration }:
                // The results the project can't show are the declared ones: a claim, listed as one, and still checked at
                // runtime by the generated validator.
                results[index] = ConfigApplication.Declared(declaration, obj.Id, types);
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

    /// <summary>
    /// A selected procedure whose results are unknowable and not declared: generation goes ahead (<c>haruspex check</c> fails it,
    /// HAR6016), but the statement is named, with what to declare.
    /// </summary>
    private void WarnOfResultsToDeclare()
    {
        for (var i = 0; i < document.Objects.Count; i++)
        {
            var obj = document.Objects[i];
            if (results[i] is not null || declaredHere.Contains(obj.Id)
                || unknowns.FirstOrDefault(u => u.Object == obj.Id && u.Path == "results" && ConfigApplication.Unknowable.Contains(u.Reason)) is not { } why)
            {
                continue;
            }

            var file = config.File.Length == 0 ? HaruspexConfig.FileName : Path.GetFileName(config.File);
            problems.Add(ConfigCodes.ResultsToDeclare.At(config.Location,
                $"{obj.Id}: its result sets are unknown: {why.Detail} ({why.At}) Declare them in {file} under procedures[\"{obj.Id}\"].results, as \"none\" or a JSON Schema, or declare the results of the procedure that runs it."));
        }
    }

    /// <summary>
    /// Values a call that isn't followed leaves unknown, under <c>INSERT … EXEC</c>: its results went into a temporary table,
    /// which is unknown after it. A declaration can't help there (what a call does to temporary tables is never known from
    /// it), but a path override can, so the warning says so. One warning per call, naming it.
    /// </summary>
    private void WarnOfValuesUnknownAfterCalls()
    {
        for (var i = 0; i < document.Objects.Count; i++)
        {
            var obj = document.Objects[i];
            if (results[i] is null)
                continue;
            foreach (var call in unknowns.Where(u => u.Object == obj.Id && u.Path != "results" && ConfigApplication.Unknowable.Contains(u.Reason)).GroupBy(u => u.Detail, StringComparer.Ordinal))
            {
                var paths = call.Select(u => u.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                problems.Add(ConfigCodes.UnknownAfterCall.At(config.Location,
                    $"{obj.Id}: {string.Join(", ", paths)} {(paths.Count == 1 ? "is" : "are")} unknown: {call.Key} A declaration can't type {(paths.Count == 1 ? "it" : "them")}: after a call that is not followed, temporary tables are unknown even when its results are declared. To type the value, override its path under \"paths\" with \"type\" and \"import\"."));
            }
        }
    }

    /// <summary>The document with every name settled, its claims listed; or, after an error anywhere, every problem.</summary>
    private Outcome<IrDocument> Named()
    {
        var settler = new NameSettler(config, types, procedureNames, applier.Names, problems);
        if (problems.Any(p => p.IsError) || settler.Settle(document with { Unknowns = [.. unknowns] }, results) is not { } named)
            return new Failed<IrDocument>([.. problems]);
        return new Succeeded<IrDocument>(named with { Overrides = [.. document.Overrides, .. claims] }, [.. problems]);
    }

    /// <summary>A declaration of results haruspex infers: an error if they differ, a warning if not (it can go).</summary>
    private void Check(ProcedureOverride entry, ResultsDeclaration declared, string id, IReadOnlyList<IrResult> inferred, IReadOnlyDictionary<string, IrObjectType> inferredTypes) =>
        problems.Add(ResultsComparison.Difference(inferred, inferredTypes, declared) is { } difference
            ? ConfigCodes.DeclarationDisagrees.At(entry.At,
                $"haruspex infers the results of {id}, and the declaration disagrees at {difference} A declaration is for results that can't be inferred; to correct inferred results, override a path under \"paths\".")
            : ConfigCodes.DeclarationAgrees.At(entry.At,
                $"the declared results agree with the inference, which already infers the results of {id}. Remove the declaration."));

    /// <summary>A declaration of a procedure that is not selected is there for its callers; with none, it does nothing.</summary>
    private void Unused(ProcedureOverride entry, string name)
    {
        if (!document.Overrides.Any(o => string.Equals(o.Via, name, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add(ConfigCodes.DeclarationUnused.At(entry.At,
                $"the declared results are not used: {name} is not selected, and no selected procedure calls it."));
        }
    }
}
