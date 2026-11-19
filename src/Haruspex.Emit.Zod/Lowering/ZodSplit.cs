using Haruspex.Collections;
using Haruspex.Emit.Zod.Model;
using Haruspex.Emit.Zod.Planning;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// The second stage for a module split into files (<see cref="IrModules"/>): the whole module's declarations, lowered once as for
/// one file, so that every name and every shared shape is what it would be there, then each put in the file of the module it
/// belongs to. A procedure's declarations are in its module; a named type's, or a shape's, are in the one module that reaches it,
/// or else in the shared file, which the modules import from and which imports nothing of theirs
/// (<c>docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md</c>).
/// </summary>
/// <remarks>
/// Whatever a declaration refers to is reached by every module that reaches it, so a module's declarations refer only to its own
/// and the shared file's, and the shared file's only to its own. Each file defines the helpers its own declarations call and
/// imports only the application's names they use, so that each file passes the linter alone.
/// </remarks>
internal static class ZodSplit
{
    /// <summary>What the shared file's header says it is, after the line that says what made it.</summary>
    private const string SharedHeader =
        "The definitions two or more modules of this config share, each written once; the modules import them from here.";

    /// <summary>Each module's file, by module, with <see cref="IrModules.Shared"/>'s when anything is shared.</summary>
    /// <param name="plan">The whole module's plan.</param>
    /// <param name="modules">The document split into modules.</param>
    /// <param name="sharedModule">The specifier a module imports the shared file by.</param>
    public static EquatableMap<string, ZodModule> Lower(ZodPlan plan, IrModules modules, string sharedModule)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(modules);
        var placed = ZodLowering.Placed(plan)
            .Select(p => (p.Declaration, Home: p.Object is { } obj ? modules.Of(obj) : modules.HomeOf(p.Types)))
            .ToList();
        var declaredIn = placed.ToDictionary(p => p.Declaration.Binding, p => p.Home, StringComparer.Ordinal);
        return placed.Select(p => p.Home).Distinct(StringComparer.Ordinal).ToEquatableMap(file => file, file =>
        {
            // In the shared file, a shape no declared name holds is exported for the modules, and marked as no public name.
            var isShared = file == IrModules.Shared;
            List<ZodDeclaration> declarations =
            [
                .. placed.Where(p => p.Home == file).Select(p => isShared && !p.Declaration.Exported ? p.Declaration with { Internal = true } : p.Declaration),
            ];
            var expressions = declarations.SelectMany(d => ZodParts.Within(d.Schema)).ToList();
            var names = expressions.OfType<ZodExpression.Name>().Select(n => n.Path).ToHashSet(StringComparer.Ordinal);
            var elsewhere = names.Where(n => declaredIn.TryGetValue(n, out var home) && home != file).Order(StringComparer.Ordinal).ToList();
            if (elsewhere.FirstOrDefault(n => isShared || declaredIn[n] != IrModules.Shared) is { } stray)
                throw new InvalidOperationException($"The file of {file} refers to {stray}, which the file of {declaredIn[stray]} declares; a module may import only the shared file.");

            var imports = plan.Imports
                .Select(i => new ZodImport(i.From, [.. i.Names.Where(names.Contains)]))
                .Where(i => i.Names.Count > 0)
                .ToList();
            if (elsewhere.Count > 0)
                imports.Add(new ZodImport(sharedModule, [.. elsewhere]));
            var folds = expressions.OfType<ZodExpression.Canonical>().Select(c => c.Fold).ToHashSet(StringComparer.Ordinal);
            var header = ZodLowering.Header(plan.Document.Source, declarations).ToList();
            if (isShared)
                header.Insert(1, SharedHeader);
            return new ZodModule(
                [.. header],
                [.. imports],
                expressions.Any(e => e is ZodExpression.JsonText),
                [.. declarations],
                folds.Count == 0 ? null : plan.Document.Folds.Where(f => folds.Contains(f.Key)).ToEquatableMap(f => f.Key, f => f.Value));
        });
    }
}
