using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>A column of a <see cref="KnownTable"/>.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Value">What it may hold: the type of every value, and why one may be null.</param>
/// <param name="Declaration">
/// The table definition that declared it (<c>CREATE TABLE</c>, <c>ALTER TABLE … ADD</c>), with its constraints, when it was
/// declared; null for a column <c>SELECT … INTO</c> made. A declared column's value comes from its declaration alone, so a
/// write never changes it: <c>NOT NULL</c> is enforced, and a write converts to the declared type. A column made by
/// <c>SELECT … INTO</c> has a type, but its nullability is SQL Server's own reading of the select, which haruspex does not
/// reproduce (observed), so each write joins with what it held.
/// </param>
/// <param name="Identity">
/// Whether it is an identity column, which an insert without a column list skips: true or false when known, null when it
/// may be (a column <c>SELECT … INTO</c> copied from one it can't see the properties of).
/// </param>
/// <param name="Json">
/// What a declared <c>nvarchar(max)</c> column holds as JSON (slice 6l), from every write to it; null when it is not traced,
/// and it reads as its declaration.
/// </param>
internal sealed record TempColumn(string Name, Value Value, Table? Declaration, bool? Identity = false, JsonSlot? Json = null)
{
    /// <summary>What a read of the column gives: its value, holding the JSON its writes put in it.</summary>
    public Value Read => Json is { } slot ? slot.Read(Value) : Value;
}
