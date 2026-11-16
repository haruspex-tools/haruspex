using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Planning;

/// <summary>
/// The first stage: decides what the module declares and in what order, and refuses a document it can't write before
/// anything is built. It reads the IR and its type graph, and knows the names TypeScript binds, but no syntax.
/// </summary>
internal static class ZodPlanner
{
    /// <summary>The plan for <paramref name="document"/>.</summary>
    /// <returns>
    /// The plan; or, when a name is not an identifier, an import clashes or a type is read both as rows and as JSON, every
    /// such refusal.
    /// </returns>
    public static Outcome<ZodPlan> Plan(IrDocument document, ZodOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        var refusals = new List<Diagnostic>();
        var graph = IrTypeGraph.Of(document);
        var imports = Imports(document, refusals);
        var usesJsonText = Nodes(document).Any(t => t is IrJsonText { Content: not null });
        var entries = Entries(document, graph, refusals);
        ParameterNames(document, refusals);
        return Outcome.Of(new ZodPlan(document, graph, options, imports, usesJsonText, entries), refusals);
    }

    /// <summary>
    /// Every procedure by id, each after the named types its results reach that no earlier procedure reached, those in the
    /// graph's order, so that a schema is declared before the schemas that use it. A cycle has no such order; its references
    /// back are getters (<see cref="ZodProperty.Getter"/>), which may name a schema declared later.
    /// </summary>
    private static EquatableArray<ZodPlanEntry> Entries(IrDocument document, IrTypeGraph graph, List<Diagnostic> refusals)
    {
        var position = graph.DependenciesFirst.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ZodPlanEntry>();
        foreach (var obj in document.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            RequireIdentifier(obj.Name, refusals);
            var reached = graph.ReachableFrom(obj.Results?.Select(r => r.Type) ?? [])
                .Where(name => !declared.Contains(name))
                .OrderBy(name => position[name])
                .ToList();
            foreach (var name in reached)
            {
                RequireIdentifier(name, refusals);

                // Scalars map differently by encoding (a date is a string in JSON, a Date in a row), so a type shared between
                // encodings would need two schemas. haruspex's front end never shares one so, but an IR is a file anything can
                // write, so it is refused, as a name that isn't an identifier is.
                if (graph.EncodingsOf(name) is not { Count: 1 } encodings)
                {
                    refusals.Add(ZodCodes.TwoEncodings.At(null, $"The type {name} is read both as rows and as JSON, so one schema can't check it."));
                    declared.Add(name);
                    continue;
                }

                entries.Add(new ZodPlanEntry.NamedType(name, document.Types[name], encodings.Single()));
                declared.Add(name);
            }

            entries.Add(new ZodPlanEntry.Procedure(obj));
        }

        return [.. entries];
    }

    /// <summary>
    /// A procedure with parameters declares their schema under its name and <c>Params</c> (<see cref="ZodModule.ParametersOf"/>),
    /// and one with <c>OUTPUT</c> parameters what they hold under its name and <c>Output</c> (<see cref="ZodModule.OutputOf"/>),
    /// names the config's naming never sees: so a procedure or type that already has one is refused, as a clash the config would
    /// have refused, naming both.
    /// </summary>
    private static void ParameterNames(IrDocument document, List<Diagnostic> refusals)
    {
        var taken = document.Objects.Select(o => o.Name).Concat(document.Types.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var obj in document.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            foreach (var (name, what) in ParameterDeclarations(obj))
            {
                if (taken.Contains(name))
                    refusals.Add(ZodCodes.ParametersNameTaken.At(null, $"The {what} of {obj.Id} are declared as {name}, which is also the name of another procedure or type; rename one of them in the config file."));
            }
        }
    }

    /// <summary>The names <paramref name="obj"/>'s parameters' declarations take, with what each holds.</summary>
    private static IEnumerable<(string Name, string What)> ParameterDeclarations(IrObject obj)
    {
        if (obj.Parameters.Count > 0)
            yield return (ZodModule.ParametersOf(obj.Name), "parameters");
        if (obj.Parameters.Any(p => p.IsOutput))
            yield return (ZodModule.OutputOf(obj.Name), "OUTPUT parameters' values");
    }

    /// <summary>
    /// One import per module the config file pointed a value at (<see cref="IrImport"/>), modules and names sorted so the
    /// output is stable. An imported name must be an identifier, come from one module only, and not be one this module
    /// defines, or two declarations would clash.
    /// </summary>
    private static EquatableArray<ZodImport> Imports(IrDocument document, List<Diagnostic> refusals)
    {
        var imports = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var import in Nodes(document).OfType<IrImport>())
        {
            if (!IrIdentifiers.IsIdentifier(import.Name) || TypeScriptNames.IsReserved(import.Name))
            {
                refusals.Add(ZodCodes.BadImportName.At(null, $"The imported name '{import.Name}' is not a valid TypeScript identifier."));
                continue;
            }

            if (modules.TryGetValue(import.Name, out var other) && other != import.From)
            {
                refusals.Add(ZodCodes.ImportedTwice.At(null, $"'{import.Name}' is imported from both '{other}' and '{import.From}'."));
                continue;
            }

            modules[import.Name] = import.From;
            if (!imports.TryGetValue(import.From, out var names))
                imports[import.From] = names = new SortedSet<string>(StringComparer.Ordinal);
            names.Add(import.Name);
        }

        var defined = document.Objects.Select(o => o.Name).Concat(document.Objects.SelectMany(o => ParameterDeclarations(o).Select(d => d.Name))).Concat(document.Types.Keys)
            .SelectMany(n => new[] { n, ZodModule.SchemaOf(n) }).Append(ZodModule.ZodName).Append(ZodModule.JsonTextHelperName).Concat(ZodModule.CanonicalNames).ToHashSet(StringComparer.Ordinal);
        foreach (var name in modules.Keys.Where(defined.Contains).Order(StringComparer.Ordinal))
            refusals.Add(ZodCodes.ImportClashes.At(null, $"The imported name '{name}' is also a name this module defines or uses."));

        return [.. imports.Select(i => new ZodImport(ModuleSpecifier(i.Key), [.. i.Value]))];
    }

    /// <summary>
    /// An IR module (<see cref="IrImport.From"/>) as an ES module specifier, in a module written beside the IR file, as the
    /// CLI writes <c>haruspex.zod.ts</c> beside <c>haruspex.ir.json</c>. The IR's path, relative to the IR file's folder and
    /// starting <c>./</c> or <c>../</c>, is already a relative specifier from that folder, and its other modules are bare
    /// specifiers that the application's module resolution looks up, so each is written as it is. A Zod module written to
    /// another folder would re-base the path here.
    /// </summary>
    private static string ModuleSpecifier(string from) => from;

    /// <summary>Every type node of the type table's fields and of the results, nested ones included.</summary>
    private static IEnumerable<IrType> Nodes(IrDocument document) =>
        document.Types.Values.SelectMany(t => t.Fields.Select(f => f.Type))
            .Concat(document.Objects.SelectMany(o => o.Results ?? []).Select(r => r.Type))
            .SelectMany(IrTypeTree.Within);

    /// <summary>
    /// Names become TypeScript identifiers. The front end only settles valid ones, but the IR is a file that anything can
    /// write, so a name that would not be an identifier is refused rather than emitted.
    /// </summary>
    private static void RequireIdentifier(string name, List<Diagnostic> refusals)
    {
        if (!IrIdentifiers.IsIdentifier(name) || TypeScriptNames.IsReserved(name))
            refusals.Add(ZodCodes.BadName.At(null, $"The IR name '{name}' is not a valid TypeScript identifier."));
    }
}
