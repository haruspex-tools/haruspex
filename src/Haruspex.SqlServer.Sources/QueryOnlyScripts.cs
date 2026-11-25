namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Which scripts add nothing to a database read from a folder: the test <c>haruspex init</c> uses to keep an application's query
/// files out of the folder it reads as the database.
/// </summary>
public static class QueryOnlyScripts
{
    /// <summary>
    /// Whether the script at <paramref name="path"/>, read alone as a folder's script is read, declares nothing and gives no
    /// error: no definition, no change to one, nothing that may disable a constraint.
    /// </summary>
    /// <remarks>
    /// The test is the folder reader's own (<c>ScriptReader</c>, into <c>SchemaDeclarations</c>), so it can't drift from what the
    /// reader takes: a query, an <c>INSERT</c> or <c>UPDATE</c>, a <c>DECLARE</c>, an <c>EXEC</c> or a <c>SET</c> the reader passes
    /// over adds nothing, while a definition, an <c>ALTER</c>, a <c>NOCHECK</c>, a bulk load or a statement that doesn't parse does.
    /// Leaving such a script out of the source could claim more than the deployed database holds; leaving out one that adds
    /// nothing changes nothing read. Read alone, a script can't be told to change an object defined in another, so a change is
    /// recorded as such, which counts.
    /// </remarks>
    public static bool Is(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        var declarations = new SchemaDeclarations();
        SqlScriptFolder.ReadInto(declarations, ScriptFile.InOrder(Path.GetDirectoryName(full) ?? ".", [Path.GetFileName(full)], listed: true));
        return declarations.IsEmpty;
    }
}
