using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources;

/// <summary>The codes of the problems found reading a folder of scripts (HAR1xxx), each explained on its page in <c>docs/diagnostics</c>.</summary>
public static class SourceCodes
{
    /// <summary>A script that doesn't parse: nothing in it is read.</summary>
    public static DiagnosticCode ParseError { get; } = new(1001, DiagnosticSeverity.Error, "The SQL doesn't parse");

    /// <summary>An object created twice, so which definition applies depends on the order the scripts are run in.</summary>
    public static DiagnosticCode DefinedTwice { get; } = new(1002, DiagnosticSeverity.Error, "An object is defined more than once");
}
