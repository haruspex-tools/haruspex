using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// A database project read from a folder of <c>.sql</c> files, ready to infer: the layers composed for the
/// command line (reading in Haruspex.SqlServer.Sources, inference in Haruspex.SqlServer.Inference, the config file's
/// selection, overrides and naming in Haruspex.Config).
/// </summary>
/// <remarks>
/// This class was once the whole front end. It is kept as the composition, here in the CLI, because
/// composing the layers is the CLI's job and no layer below may know both how a model is read and how it is
/// inferred.
/// </remarks>
internal sealed class SqlProject
{
    private readonly DatabaseModel model;

    private SqlProject(DatabaseModel model, IReadOnlyList<Diagnostic> diagnostics)
    {
        this.model = model;
        Diagnostics = diagnostics;
        Procedures = [.. model.Procedures.Select(p => p.Name)];
    }

    /// <summary>Errors found while reading the project. Results inferred despite them are not trustworthy.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Every procedure in the project, in a stable order.</summary>
    public IReadOnlyList<ObjectName> Procedures { get; }

    /// <summary>Reads every <c>*.sql</c> file under <paramref name="directory"/>.</summary>
    public static SqlProject Load(string directory)
    {
        var read = SqlScriptFolder.Read(directory);
        return new SqlProject(read.Model, read.Diagnostics);
    }

    /// <summary>
    /// The procedures that match any of the patterns, and the patterns that match none. A pattern that matches
    /// nothing is reported, not ignored: a typo would otherwise quietly drop an interface from the generated code.
    /// </summary>
    public Selection Select(IReadOnlyList<ObjectPattern> include)
    {
        ArgumentNullException.ThrowIfNull(include);
        var (procedures, _) = ProcedureSelection.Select(Procedures, include, HaruspexConfig.None("."));
        return new Selection(procedures, [.. include.Where(i => !Procedures.Any(i.Matches))]);
    }

    /// <summary>
    /// Infers the IR for the given procedures and settles their names with the default naming rules, as when there is no
    /// config file.
    /// </summary>
    /// <exception cref="InvalidOperationException">The default rules cannot name them (two names clash).</exception>
    public IrDocument Infer(IReadOnlyList<ObjectName> selected) => Infer(selected, warnings: null);

    /// <summary>
    /// Infers the IR for the given procedures and settles their names with the default naming rules, as when there is no
    /// config file, adding to <paramref name="warnings"/> what inference could not check.
    /// </summary>
    /// <exception cref="InvalidOperationException">The default rules cannot name them (two names clash).</exception>
    public IrDocument Infer(IReadOnlyList<ObjectName> selected, ICollection<Diagnostic>? warnings)
    {
        var applied = Infer(selected, HaruspexConfig.None("."), warnings: warnings);
        return applied.Document ?? throw new InvalidOperationException(string.Join(Environment.NewLine, applied.Problems));
    }

    /// <summary>Infers the IR for the given procedures, and applies the config file's overrides and naming to it.</summary>
    /// <param name="selected">The procedures to infer.</param>
    /// <param name="config">The config.</param>
    /// <param name="selectedByFile">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <param name="outputDirectory">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <param name="warnings">
    /// Where to add what inference could not check, such as a write to a table the project does not define, whose triggers
    /// it cannot see. They do not stop generation.
    /// </param>
    /// <remarks>
    /// The config file's declared results (<c>procedures["s.p"].results</c>) are handed to inference, in the IR's terms, so that
    /// a call of a declared procedure contributes them to its caller; a procedure's own declaration is applied by the config
    /// layer, which checks it against what is inferred. The declared procedures of the project that are not selected are
    /// inferred too, for that check alone.
    /// </remarks>
    public ConfigApplied Infer(IReadOnlyList<ObjectName> selected, HaruspexConfig config, IReadOnlyCollection<ObjectName>? selectedByFile = null, string? outputDirectory = null, ICollection<Diagnostic>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(config);
        var declarations = new Dictionary<ObjectName, DeclaredResults>();
        foreach (var entry in config.Procedures)
        {
            if (entry is { Results: { } declared, Procedure.Name: { } name })
                declarations.TryAdd(name, new DeclaredResults(declared.Results, declared.Types, declared.Why));
        }

        var document = IrInference.Infer(model, selected, declarations, warnings);
        var others = declarations.Keys
            .Select(name => model.FindProcedure(name)?.Name)
            .OfType<ObjectName>()
            .Where(name => !selected.Contains(name))
            .Distinct()
            .ToList();
        var declaredDocument = others.Count == 0 ? null : IrInference.Infer(model, others, declarations);
        return ConfigApplication.Apply(document, config, Procedures, selectedByFile, outputDirectory, declaredDocument);
    }
}
