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
        // An override that makes a set exact leaves its fold unnamed, and declared results may name one only their document holds.
        return new ConfigApplying(document, config, procedures, selectedByFile, from => Import(from, config.Directory, outputDirectory), declaredDocument).Apply()
            .Select(applied => IrDocumentFolds.Settled(applied, declaredDocument is null ? [] : [declaredDocument.Folds]));
    }

    /// <summary>
    /// The reasons for results that the project cannot show, which only a declaration can supply. <see cref="Apply"/> reports
    /// every unknown with one of them, with what to declare (<see cref="ConfigCodes.ResultsToDeclare"/>,
    /// <see cref="ConfigCodes.UnknownAfterCall"/>), so nothing else needs to.
    /// </summary>
    public static IReadOnlySet<IrUnknownReason> Declarable => Unknowable;

    internal static readonly HashSet<IrUnknownReason> Unknowable =
    [
        IrUnknownReason.DynamicSql,
        IrUnknownReason.ProcedureVariable,
        IrUnknownReason.SystemProcedure,
        IrUnknownReason.MissingProcedure,
        IrUnknownReason.AmbiguousProcedure,
        IrUnknownReason.ExternalCode,
        IrUnknownReason.EncryptedDefinition,
    ];

    /// <summary>
    /// A declaration's results for the procedure <paramref name="id"/>, its types added to <paramref name="types"/> under keys
    /// of that procedure (the declaration's own keys were made from the config file's spelling of its name).
    /// </summary>
    internal static EquatableArray<IrResult> Declared(ResultsDeclaration declaration, string id, Dictionary<string, IrObjectType> types)
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
