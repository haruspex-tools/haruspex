using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The module a run would write, in memory: the document with the config applied, and the files emitted from it, with
/// what <see cref="Generate"/> needs to write them and <see cref="Check"/> to compare them. With a baseline, the document is the
/// database's own combined with the baseline's (<see cref="CombinedTypes"/>), and the database's own is kept beside it. Like each
/// value handed from one step to the next, a class: nothing compares two.
/// </summary>
internal sealed class EmittedModule(HaruspexConfig config, string @out, SqlProject project, IrDocument document, EquatableArray<EmittedFile> files)
{
    /// <summary>The baseline the run was given, read; null when there is none.</summary>
    public Baseline? Baseline { get; init; }

    /// <summary>
    /// The document of the module's own database, the source alone, with the config applied: <see cref="Document"/> but where a
    /// baseline was combined with it.
    /// </summary>
    public IrDocument Database { get; init; } = document;

    /// <summary>
    /// What combining the database's document with the baseline's found, each at its procedure: an error for each change that
    /// can't be bridged, whose procedure the module leaves out, and warnings. Kept apart from the steps' outcomes, since the
    /// module is written all the same; empty without a baseline.
    /// </summary>
    public EquatableArray<Diagnostic> Combination { get; init; } = [];

    /// <summary>The config file the module was made with, or the defaults when there is none.</summary>
    public HaruspexConfig Config { get; } = config;

    /// <summary>The output folder, where the module is written or committed.</summary>
    public string Out { get; } = @out;

    /// <summary>The project the module was inferred from, which roots its locations.</summary>
    public SqlProject Project { get; } = project;

    /// <summary>The IR, with the config file's overrides and names applied.</summary>
    public IrDocument Document { get; } = document;

    /// <summary>The files the config file's emitters give, in its order (<see cref="HaruspexConfig.Emitters"/>).</summary>
    public EquatableArray<EmittedFile> Files { get; } = files;

    /// <summary>The modules the config file splits the output into, in ordinal order; empty when it doesn't split it.</summary>
    public EquatableArray<string> Modules { get; init; } = [];

    /// <summary>Where the IR is written.</summary>
    public string IrPath => Path.Join(Out, HaruspexCli.IrFileName);

    /// <summary>Where <paramref name="file"/> is written.</summary>
    public string PathOf(EmittedFile file) => Path.Join(Out, file.Name);
}
