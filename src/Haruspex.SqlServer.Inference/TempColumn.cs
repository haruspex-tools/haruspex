using Haruspex.Diagnostics;
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
/// What a declared <c>nvarchar(max)</c> column holds as JSON, from every write to it; null when it is not traced,
/// and it reads as its declaration.
/// </param>
internal sealed record TempColumn(string Name, Value Value, DeclaredTable? Declaration, bool? Identity = false, JsonSlot? Json = null)
{
    /// <summary>What a read of the column gives: its value, holding the JSON its writes put in it.</summary>
    public Value Read => Json is { } slot ? slot.Read(Value) : Value;

    /// <summary>
    /// The same column on either of two paths, each fact joined in its own lattice: its value (<see cref="Value.Join"/>), its
    /// JSON (<see cref="JsonSlot.Join"/>, untraced when either path doesn't trace it), and its declaration and identity, each
    /// kept when both paths agree and otherwise not known. A column declared on some paths only, or differently, is not
    /// guaranteed by either declaration, so it joins later writes like a column <c>SELECT … INTO</c> made, and is never read
    /// again from one path's declaration.
    /// </summary>
    /// <param name="at">Where the paths meet.</param>
    /// <param name="what">The column's name with its table's, for an unknown.</param>
    public static TempColumn Join(TempColumn c, TempColumn d, SourceLocation at, string what) => c with
    {
        Value = Value.Join(c.Value, d.Value, at, what),
        Declaration = ReferenceEquals(c.Declaration, d.Declaration) ? c.Declaration : null,
        Identity = c.Identity == d.Identity ? c.Identity : null,
        Json = c.Json is { } x && d.Json is { } y ? JsonSlot.Join(x, y) : null,
    };
}
