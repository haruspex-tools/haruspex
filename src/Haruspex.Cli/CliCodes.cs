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
    /// The config file's <c>references</c> beside a source that is a folder of scripts, which has no references to find: they are read
    /// only with a DACPAC or a project file.
    /// </summary>
    public static DiagnosticCode ReferencesUnused { get; } = new(6009, DiagnosticSeverity.Warning, "References with a folder of scripts");

    /// <summary>
    /// The config file's <c>collation</c> beside a DACPAC that records another: the package's is the database's, so it is the
    /// one used.
    /// </summary>
    public static DiagnosticCode CollationOverruled { get; } = new(6010, DiagnosticSeverity.Warning, "A collation the package records otherwise");

    /// <summary><c>check</c> finds no committed IR, module or both in the output folder, so there is nothing to compare with.</summary>
    public static DiagnosticCode NothingCommitted { get; } = new(6011, DiagnosticSeverity.Error, "Nothing committed to check against");

    /// <summary>The committed IR is of a version this haruspex doesn't read, so no claim can be compared.</summary>
    public static DiagnosticCode CommittedOtherVersion { get; } = new(6012, DiagnosticSeverity.Error, "The committed IR is of another version");

    /// <summary>The committed IR can't be read: not JSON, or not an IR document.</summary>
    public static DiagnosticCode CommittedUnreadable { get; } = new(6013, DiagnosticSeverity.Error, "The committed IR can't be read");

    /// <summary>What the module claims would change: one diagnostic per change, at the procedure.</summary>
    public static DiagnosticCode ClaimChanged { get; } = new(6014, DiagnosticSeverity.Error, "What the module claims would change");

    /// <summary>The committed module isn't what haruspex writes, though the IR claims the same.</summary>
    public static DiagnosticCode ModuleDiffers { get; } = new(6015, DiagnosticSeverity.Error, "The committed module isn't what haruspex writes");

    /// <summary>A selected procedure's result sets are unknown and not declared, which <c>check</c> doesn't pass.</summary>
    public static DiagnosticCode ResultsUnknown { get; } = new(6016, DiagnosticSeverity.Error, "A selected procedure's result sets are unknown");

    /// <summary>The module has unknowns, and the config file's <c>check.unknowns</c> is <c>fail</c>.</summary>
    public static DiagnosticCode UnknownsFail { get; } = new(6017, DiagnosticSeverity.Error, "Unknowns the config file fails");

    /// <summary><c>--all</c> finds no config file below its folder, or the folder isn't there.</summary>
    public static DiagnosticCode NoConfigFound { get; } = new(6018, DiagnosticSeverity.Error, "No config file below the folder");

    /// <summary>
    /// Options that can't go together: <c>--all</c> and <c>--config</c>, or a source, <c>--include</c> or <c>--out</c>, which are for one
    /// module, with several.
    /// </summary>
    public static DiagnosticCode OptionsApart { get; } = new(6019, DiagnosticSeverity.Error, "Options that can't go together");

    /// <summary>Two modules of one run write to the same output folder.</summary>
    public static DiagnosticCode SharedOut { get; } = new(6020, DiagnosticSeverity.Error, "Two modules write to one folder");

    /// <summary><c>init</c> finds a config file already in the working directory, which it doesn't write over.</summary>
    public static DiagnosticCode ConfigExists { get; } = new(6021, DiagnosticSeverity.Error, "A config file is already there");

    /// <summary><c>init</c> finds no project file, DACPAC or <c>.sql</c> file near the working directory.</summary>
    public static DiagnosticCode NoSourceFound { get; } = new(6022, DiagnosticSeverity.Error, "No database is found to read");

    /// <summary><c>init</c> finds more than one project file, or more than one DACPAC, and can't tell which is the application's.</summary>
    public static DiagnosticCode SourceAmbiguous { get; } = new(6023, DiagnosticSeverity.Error, "More than one database to read");

    /// <summary>
    /// The codes with which <c>check</c> fails: what it found differs from what is committed, or can't be compared with it, or is
    /// incomplete. Each makes the exit code 1, not 2: the command line and the config file may be right.
    /// </summary>
    public static IReadOnlySet<DiagnosticCode> CheckFailures { get; } = new HashSet<DiagnosticCode>
    {
        NothingCommitted, CommittedOtherVersion, CommittedUnreadable, ClaimChanged, ModuleDiffers, ResultsUnknown, UnknownsFail,
    };
}
