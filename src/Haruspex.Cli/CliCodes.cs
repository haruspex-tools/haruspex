using Haruspex.Diagnostics;

namespace Haruspex.Cli;

/// <summary>
/// The codes of the problems the command line itself finds (HAR6xxx), each explained on its page in
/// <c>docs/diagnostics</c>: what it was asked to do, and the files it reads and writes.
/// </summary>
internal static class CliCodes
{
    /// <summary>The command line doesn't parse: an unknown option, a missing value.</summary>
    public static DiagnosticCode BadCommandLine { get; } = new(6001, DiagnosticSeverity.Error, "The command line doesn't parse");

    /// <summary>The config file named with <c>--config</c> doesn't exist.</summary>
    public static DiagnosticCode ConfigNotFound { get; } = new(6002, DiagnosticSeverity.Error, "The config file isn't there");

    /// <summary>Neither the command line nor the config file says which folder of SQL files to read.</summary>
    public static DiagnosticCode NoSource { get; } = new(6003, DiagnosticSeverity.Error, "No folder of SQL files is given");

    /// <summary>The folder of SQL files doesn't exist.</summary>
    public static DiagnosticCode SourceNotFound { get; } = new(6004, DiagnosticSeverity.Error, "The folder of SQL files isn't there");

    /// <summary>Neither the command line nor the config file says where to write.</summary>
    public static DiagnosticCode NoOut { get; } = new(6005, DiagnosticSeverity.Error, "No output folder is given");

    /// <summary>An <c>--include</c> pattern that isn't well formed.</summary>
    public static DiagnosticCode BadInclude { get; } = new(6006, DiagnosticSeverity.Error, "An --include pattern isn't well formed");

    /// <summary>Nothing is selected: no <c>--include</c>, and no schema of the config file includes anything.</summary>
    public static DiagnosticCode NothingSelected { get; } = new(6007, DiagnosticSeverity.Error, "Nothing is selected");

    /// <summary>The output can't be written: a full disk, a folder that can't be written to.</summary>
    public static DiagnosticCode CannotWrite { get; } = new(6008, DiagnosticSeverity.Error, "The output can't be written");
}
