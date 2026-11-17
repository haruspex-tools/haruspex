using Haruspex.Collections;
using Haruspex.Diagnostics;

namespace Haruspex.Cli;

/// <summary>
/// What a whole run found: each module's report, in the order they ran, or why none ran (options that can't go together,
/// <c>--all</c> finding nothing, two modules sharing a folder).
/// </summary>
/// <remarks>Like each value handed from one step to the next, a class: nothing compares two.</remarks>
/// <param name="command">The command that ran.</param>
/// <param name="modules">Each module's report.</param>
/// <param name="several">Whether the run covers several modules, which a text report then names one by one.</param>
/// <param name="refused">Why no module ran; empty when they did.</param>
internal sealed class RunReport(CommandName command, IReadOnlyList<ModuleReport> modules, bool several, EquatableArray<Diagnostic> refused)
{
    /// <summary>The command that ran.</summary>
    public CommandName Command { get; } = command;

    /// <summary>Each module's report, in the order they ran.</summary>
    public IReadOnlyList<ModuleReport> Modules { get; } = modules;

    /// <summary>Whether the run covers several modules, which a text report then names one by one.</summary>
    public bool Several { get; } = several;

    /// <summary>Why no module ran; empty when they did.</summary>
    public EquatableArray<Diagnostic> Refused { get; } = refused;

    /// <summary>The run's exit code: a usage error when it was refused, otherwise the worst of its modules'.</summary>
    public int ExitCode => Refused.Count > 0
        ? HaruspexCli.ExitCodes.Usage
        : Modules.Select(m => m.ExitCode).DefaultIfEmpty(HaruspexCli.ExitCodes.Success).Max();
}
