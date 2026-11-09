using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// A database project read from a folder of <c>.sql</c> files, ready to infer: the layers composed for the
/// command line (reading in Haruspex.SqlServer.Sources, inference in Haruspex.SqlServer.Inference).
/// </summary>
/// <remarks>
/// Before slice 2b this class was the whole front end. It is kept as the composition, here in the CLI, because
/// composing the layers is the CLI's job and no layer below may know both how a model is read and how it is
/// inferred. Selecting by pattern will move to Haruspex.Config when that exists.
/// </remarks>
public sealed class SqlProject
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
        var selected = Procedures.Where(p => include.Any(i => i.Matches(p))).ToList();
        var unmatched = include.Where(i => !Procedures.Any(i.Matches)).ToList();
        return new Selection(selected, unmatched);
    }

    /// <summary>Infers the IR for the given procedures.</summary>
    public IrDocument Infer(IReadOnlyList<ObjectName> selected) => IrInference.Infer(model, selected);
}

/// <summary>The outcome of <see cref="SqlProject.Select"/>.</summary>
public sealed record Selection(IReadOnlyList<ObjectName> Procedures, IReadOnlyList<ObjectPattern> Unmatched);
