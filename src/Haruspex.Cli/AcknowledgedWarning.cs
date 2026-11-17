using Haruspex.Config;
using Haruspex.Diagnostics;

namespace Haruspex.Cli;

/// <summary>A warning a run gave, and the config file's entry that acknowledges it (<see cref="Acknowledgements"/>).</summary>
/// <param name="Warning">The warning.</param>
/// <param name="By">The entry that acknowledges it, with why.</param>
internal sealed record AcknowledgedWarning(Diagnostic Warning, Acknowledgement By);
