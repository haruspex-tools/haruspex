using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources;

/// <summary>The codes of the problems found reading a folder of scripts or a DACPAC (HAR1xxx), each explained on its page in <c>docs/diagnostics</c>.</summary>
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
    /// A function or view created <c>WITH ENCRYPTION</c> whose text the DACPAC doesn't hold, as one extracted from a database
    /// doesn't: it is left out of the model, so what reads it is unknown.
    /// </summary>
    public static DiagnosticCode EncryptedNotRead { get; } = new(1006, DiagnosticSeverity.Warning, "An encrypted function or view whose text the DACPAC lacks");
}
