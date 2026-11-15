using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources;

/// <summary>The codes of the problems found reading a folder of scripts, a DACPAC or a project (HAR1xxx), each explained on its page in <c>docs/diagnostics</c>.</summary>
public static class SourceCodes
{
    /// <summary>A script that doesn't parse: nothing in it is read.</summary>
    public static DiagnosticCode ParseError { get; } = new(1001, DiagnosticSeverity.Error, "The SQL doesn't parse");

    /// <summary>An object created twice, so which definition applies depends on the order the scripts are run in.</summary>
    public static DiagnosticCode DefinedTwice { get; } = new(1002, DiagnosticSeverity.Error, "An object is defined more than once");

    /// <summary>
    /// A DACPAC a package refers to that is found nowhere looked: the objects it would give are missing from the model, and a name
    /// that binds to nothing says it may be defined there. One warning for the package, not one for each name.
    /// </summary>
    public static DiagnosticCode ReferenceNotFound { get; } = new(1003, DiagnosticSeverity.Warning, "A referenced DACPAC isn't found");

    /// <summary>Elements of a DACPAC that haruspex doesn't model, counted by type, so that none is passed over in silence.</summary>
    public static DiagnosticCode NotModelled { get; } = new(1004, DiagnosticSeverity.Note, "Elements of a DACPAC that aren't modelled");

    /// <summary>A file given or referred to as a DACPAC that can't be read as one: nothing in it is read.</summary>
    public static DiagnosticCode UnreadablePackage { get; } = new(1005, DiagnosticSeverity.Error, "A file that can't be read as a DACPAC");

    /// <summary>
    /// A database project a project refers to, of the same database, that isn't found: the objects it would give are missing from
    /// the model, and a name that binds to nothing says it may be defined there. One warning for the project, as for a package.
    /// </summary>
    public static DiagnosticCode ProjectNotFound { get; } = new(1007, DiagnosticSeverity.Warning, "A referenced project isn't found");

    /// <summary>
    /// Part of a project file that haruspex doesn't evaluate or understand, and that touches what it reads: the scripts, a script's
    /// settings, or the database's; that part isn't read, never guessed (decision 0044).
    /// </summary>
    public static DiagnosticCode PartNotRead { get; } = new(1008, DiagnosticSeverity.Warning, "Part of a project file isn't read");

    /// <summary>A script a project builds that isn't on disk: a build would stop there, and nothing in it is read.</summary>
    public static DiagnosticCode ScriptNotFound { get; } = new(1009, DiagnosticSeverity.Error, "A script a project builds isn't found");

    /// <summary>A file given or referred to as a database project that can't be read as one: nothing in it is read.</summary>
    public static DiagnosticCode UnreadableProject { get; } = new(1010, DiagnosticSeverity.Error, "A file that can't be read as a database project");

    /// <summary>
    /// A reference a project makes that haruspex doesn't read yet (another database, a DACPAC, a package): whatever is named in it
    /// is unknown, exactly as when the scripts are read as a folder.
    /// </summary>
    public static DiagnosticCode ReferenceNotRead { get; } = new(1011, DiagnosticSeverity.Note, "A project's reference isn't read yet");
}
