using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// What one module's run of <c>generate</c> or <c>check</c> found and did, kept apart from how it is printed: the commands
/// compute it, and a report (<see cref="Output.TextReport"/>, or one of the machine formats) writes it. So the same run reads
/// the same in every format, and what a format leaves out is that format's choice, not the command's.
/// </summary>
/// <remarks>Like each value handed from one step to the next, a class: nothing compares two.</remarks>
/// <param name="command">The command that ran.</param>
/// <param name="request">What the module's run was asked.</param>
/// <param name="diagnostics">Everything the run found, in the order it found it, notes for unknowns included.</param>
/// <param name="exitCode">The module's exit code (<see cref="HaruspexCli.ExitCodes"/>).</param>
internal sealed class ModuleReport(CommandName command, RunRequest request, EquatableArray<Diagnostic> diagnostics, int exitCode)
{
    /// <summary>The command that ran.</summary>
    public CommandName Command { get; } = command;

    /// <summary>Everything the run found, in the order it found it, notes for unknowns included.</summary>
    public EquatableArray<Diagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>The module's exit code (<see cref="HaruspexCli.ExitCodes"/>).</summary>
    public int ExitCode { get; } = exitCode;

    /// <summary>
    /// The module's config file, full, as <c>--config</c> named it or <c>--all</c> found it; null when the run looked for one,
    /// which only a run of one module does.
    /// </summary>
    public string? Config { get; } = request.Config is { } config ? request.Resolve(config) : null;

    /// <summary>The output folder, full, once the run has settled it; null when it failed before.</summary>
    public string? Out { get; init; }

    /// <summary>The baseline the run was given, full: what <c>generate</c> combined the module with, or <c>check</c> judged it against; null when there was none.</summary>
    public string? Baseline { get; init; }

    /// <summary>The module's IR, once inferred; null when the run failed before.</summary>
    public IrDocument? Document { get; init; }

    /// <summary>The files <c>generate</c> wrote, full, the IR first; empty for <c>check</c> and for a run that wrote nothing.</summary>
    public EquatableArray<string> Written { get; init; } = [];

    /// <summary>The modules the output is split into, when the config file splits it (<c>split</c>); empty otherwise.</summary>
    public EquatableArray<string> Modules { get; init; } = [];

    /// <summary>Each unknown in the module, with its note (also among <see cref="Diagnostics"/>); empty when there is no module.</summary>
    public EquatableArray<NotedUnknown> Unknowns { get; init; } = [];

    /// <summary>What changed since <c>generate</c> last wrote the module; null for <c>check</c>, and for a run that wrote nothing.</summary>
    public SinceLastRun? Since { get; init; }

    /// <summary>The warnings the config file acknowledges, set aside from <see cref="Diagnostics"/>: counted, and listed with <c>--verbose</c>.</summary>
    public EquatableArray<AcknowledgedWarning> Acknowledged { get; init; } = [];

    /// <summary>Whether the module succeeded.</summary>
    public bool Succeeded => ExitCode == HaruspexCli.ExitCodes.Success;
}
