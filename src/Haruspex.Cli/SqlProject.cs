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
/// It is the composition, here in the CLI, because composing the layers is the CLI's job and no layer below may know
/// both how a model is read and how it is inferred.
/// </remarks>
internal sealed class SqlProject
{
    private readonly DatabaseModel model;

    private SqlProject(DatabaseModel model, IReadOnlyList<Diagnostic> diagnostics)
    {
        this.model = model;
        Diagnostics = diagnostics;
        // In name order, as a listing is: the model's own order is where each was read, which would make the order the
        // procedures are inferred in, and so of the warnings they give, depend on the files' names.
        Procedures = [.. model.Procedures.Select(p => p.Name).OrderBy(n => n.ToString(), StringComparer.Ordinal)];
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
        return new Selection([.. Procedures.Where(p => include.Any(i => i.Matches(p)))], [.. include.Where(i => !Procedures.Any(i.Matches))]);
    }

    /// <summary>
    /// Infers the IR for the given procedures and settles their names with the default naming rules, as when there is no
    /// config file.
    /// </summary>
    /// <returns>The document, with what inference could not check; or, when the default rules can't name them all (two names clash), why.</returns>
    public Outcome<IrDocument> Infer(IReadOnlyList<ObjectName> selected) => Infer(selected, HaruspexConfig.None("."));

    /// <summary>Infers the IR for the given procedures, and applies the config file's overrides and naming to it.</summary>
    /// <param name="selected">The procedures to infer.</param>
    /// <param name="config">The config.</param>
    /// <param name="selectedByFile">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <param name="outputDirectory">See <see cref="ConfigApplication.Apply"/>.</param>
    /// <returns>
    /// The document with the config applied, after what inference could not check (such as a write to a table the project
    /// does not define, whose triggers it cannot see), which doesn't stop generation; or every problem.
    /// </returns>
    /// <remarks>
    /// The config file's declared results (<c>procedures["s.p"].results</c>) are handed to inference, in the IR's terms, so that
    /// a call of a declared procedure contributes them to its caller; a procedure's own declaration is applied by the config
    /// layer, which checks it against what is inferred. The declared procedures of the project that are not selected are
    /// inferred too, for that check alone.
    /// </remarks>
    public Outcome<IrDocument> Infer(IReadOnlyList<ObjectName> selected, HaruspexConfig config, IReadOnlyCollection<ObjectName>? selectedByFile = null, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(config);
        var declarations = new Dictionary<ObjectName, DeclaredResults>();
        foreach (var entry in config.Procedures)
        {
            if (entry is DeclaredResultsOverride { Results: var declared, Procedure: var name })
                declarations.TryAdd(name, new DeclaredResults(declared.Results, declared.IrTypes, declared.Why));
        }

        var inferred = IrInference.Infer(model, selected, declarations, config.Session);
        var others = declarations.Keys
            .Select(name => model.FindProcedure(name)?.Name)
            .OfType<ObjectName>()
            .Where(name => !selected.Contains(name))
            .Distinct()
            .ToList();

        // Inferred only to check their declarations against: what inference couldn't check of them is not this run's to report.
        var declaredDocument = others.Count == 0 ? null : IrInference.Infer(model, others, declarations, config.Session).Value;
        return inferred.Then(document => ConfigApplication.Apply(document, config, Procedures, selectedByFile, outputDirectory, declaredDocument));
    }
}
