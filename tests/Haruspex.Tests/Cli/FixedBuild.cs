using Haruspex.Cli;

namespace Haruspex.Tests.Cli;

/// <summary>
/// A build of haruspex that never moves, for a test whose output is a reviewed snapshot: every IR file the command writes names the
/// build that runs it (<see cref="Haruspex.Ir.IrSource.WrittenBy"/>), and the build the tests run is made from the commit checked
/// out, so a snapshot of what it writes would change with every commit though nothing it claims did. Its commit is all zeros, which
/// names no commit, so a snapshot can't be taken for one a real build wrote.
/// Why: docs/decisions/0117-an-ir-document-names-the-build-that-made-its-claims.md.
/// </summary>
internal static class FixedBuild
{
    /// <summary>The version the build says, as a real one says it: the version, a <c>+</c> and the whole commit.</summary>
    public const string Version = "0.0.0-test+0000000000000000000000000000000000000000";

    /// <summary>Neither stream a terminal, no variable set, and this build: <see cref="Terminal.Plain"/> with the build fixed.</summary>
    public static Terminal Terminal { get; } = new() { Version = Version };
}
