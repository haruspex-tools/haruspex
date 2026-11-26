using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>What <see cref="SqlScriptFolder"/> found in a project's scripts.</summary>
/// <param name="Model">The schema. When there are diagnostics it is incomplete: a file that does not parse adds nothing.</param>
/// <param name="Diagnostics">Errors found while reading, such as parse errors and duplicate definitions.</param>
public sealed record SchemaReadResult(DatabaseModel Model, EquatableArray<Diagnostic> Diagnostics)
{
    /// <summary>
    /// For a read of several folders (<see cref="SqlScriptFolder.ReadFolders"/>), each folder as found on disk, relative to the folder
    /// the list is written in and <c>/</c>-separated, in the order listed: the names the positions in the model begin with, whatever
    /// case the list wrote them in. Empty for any other read.
    /// </summary>
    public EquatableArray<string> Folders { get; init; } = [];
}
