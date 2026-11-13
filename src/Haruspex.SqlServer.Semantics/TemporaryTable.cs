using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A temporary table, <c>#t</c> or <c>##t</c>. A procedure can create a given <c>#t</c> once at most (observed:
/// A_procedure_cannot_create_a_temporary_table_twice_even_after_dropping_it), so the name always means the one the body
/// creates, or its caller's when it creates none; but SQL Server resolves it when the statement runs, so which columns it
/// has there (whether the creation was reached, what an <c>ALTER TABLE</c> did) is the walk's to say, and the binder takes
/// them as given (<see cref="NameEnvironment.WithTemporaryColumns"/>).
/// </summary>
/// <param name="Name">Its name, with its sigil.</param>
/// <param name="Columns">Its columns where it is read, as the walk knows them; null when it doesn't.</param>
public sealed record TemporaryTable(string Name, EquatableArray<string>? Columns) : RelationSymbol(Name, Columns)
{
    /// <summary>The table's name as the walk keys what it holds: <see cref="Binder.Temporary"/>'s.</summary>
    public LocalName Local => new(Name);
}
