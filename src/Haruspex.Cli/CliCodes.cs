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

    /// <summary>Neither the command line nor the config file says which folder of SQL files or DACPAC to read.</summary>
    public static DiagnosticCode NoSource { get; } = new(6003, DiagnosticSeverity.Error, "No source is given");

    /// <summary>The folder of SQL files, the project file or the DACPAC named doesn't exist.</summary>
    public static DiagnosticCode SourceNotFound { get; } = new(6004, DiagnosticSeverity.Error, "The source isn't there");

    /// <summary>Neither the command line nor the config file says where to write.</summary>
    public static DiagnosticCode NoOut { get; } = new(6005, DiagnosticSeverity.Error, "No output folder is given");

    /// <summary>An <c>--include</c> pattern that isn't well formed.</summary>
    public static DiagnosticCode BadInclude { get; } = new(6006, DiagnosticSeverity.Error, "An --include pattern isn't well formed");

    /// <summary>Nothing is selected: no <c>--include</c>, and no schema of the config file includes anything.</summary>
    public static DiagnosticCode NothingSelected { get; } = new(6007, DiagnosticSeverity.Error, "Nothing is selected");

    /// <summary>The output can't be written: a full disk, a folder that can't be written to.</summary>
    public static DiagnosticCode CannotWrite { get; } = new(6008, DiagnosticSeverity.Error, "The output can't be written");

    /// <summary>
    /// The config file's <c>references</c> beside a source that is a folder of scripts, which has no references to find, or a
    /// project file, which says where its own are: they are read only with a DACPAC.
    /// </summary>
    public static DiagnosticCode ReferencesUnused { get; } = new(6009, DiagnosticSeverity.Warning, "References with a source that isn't a DACPAC");

    /// <summary>
    /// The config file's <c>collation</c> beside a DACPAC that records another: the package's is the database's, so it is the
    /// one used.
    /// </summary>
    public static DiagnosticCode CollationOverruled { get; } = new(6010, DiagnosticSeverity.Warning, "A collation the package records otherwise");
}
