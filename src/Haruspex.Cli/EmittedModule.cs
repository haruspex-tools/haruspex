using Haruspex.Config;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The module a run would write, in memory: the document with the config applied, and the Zod module emitted from it, with
/// what <see cref="Generate"/> needs to write them and <see cref="Check"/> to compare them. Like each value handed from one step
/// to the next, a class: nothing compares two.
/// </summary>
internal sealed class EmittedModule(HaruspexConfig config, string @out, SqlProject project, IrDocument document, string zod)
{
    /// <summary>The config file the module was made with, or the defaults when there is none.</summary>
    public HaruspexConfig Config { get; } = config;

    /// <summary>The output folder, where the module is written or committed.</summary>
    public string Out { get; } = @out;

    /// <summary>The project the module was inferred from, which roots its locations.</summary>
    public SqlProject Project { get; } = project;

    /// <summary>The IR, with the config file's overrides and names applied.</summary>
    public IrDocument Document { get; } = document;

    /// <summary>The Zod module's text.</summary>
    public string Zod { get; } = zod;

    /// <summary>Where the IR is written.</summary>
    public string IrPath => Path.Join(Out, HaruspexCli.IrFileName);

    /// <summary>Where the module is written.</summary>
    public string ZodPath => Path.Join(Out, HaruspexCli.ZodFileName);
}
