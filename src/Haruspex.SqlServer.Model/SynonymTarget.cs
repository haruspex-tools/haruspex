namespace Haruspex.SqlServer.Model;

/// <summary>
/// The name a synonym stands for, as written: up to four parts, of which only the last must be given. A name with a server
/// or a database is outside the project.
/// </summary>
/// <param name="Server">The linked server, or null when the name has none.</param>
/// <param name="Database">The database, or null when the name has none.</param>
/// <param name="Schema">
/// The schema, or null when the name has none, so that SQL Server looks in the synonym's schema and then in <c>dbo</c>, whoever
/// reads it (see <c>SchemaSearch</c> in Haruspex.SqlServer.Semantics).
/// </param>
/// <param name="Name">The object's name.</param>
/// <param name="Text">The whole name as written, for messages.</param>
public sealed record SynonymTarget(string? Server, string? Database, string? Schema, string Name, string Text)
{
    /// <summary>The name as written.</summary>
    public override string ToString() => Text;
}
