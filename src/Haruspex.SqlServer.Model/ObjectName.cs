using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A two-part object name, <c>schema.name</c>. Compared case-insensitively: identifiers are in a
/// case-insensitive collation in almost every SQL Server database, and treating two spellings as one object
/// can only merge names, never invent a type.
/// </summary>
/// <param name="Schema">The schema, such as <c>dbo</c>.</param>
/// <param name="Name">The object's name within its schema.</param>
public readonly record struct ObjectName(string Schema, string Name)
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether both parts are equal, ignoring case.</summary>
    public bool Equals(ObjectName other) => Comparer.Equals(Schema, other.Schema) && Comparer.Equals(Name, other.Name);

    /// <summary>A hash code consistent with the case-insensitive <see cref="Equals(ObjectName)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(Comparer.GetHashCode(Schema ?? ""), Comparer.GetHashCode(Name ?? ""));

    /// <summary>The name as written in T-SQL, bracketing a part only where it must be (a dot or bracket in it).</summary>
    public override string ToString() => Quote(Schema) + "." + Quote(Name);

    /// <summary>
    /// The name a definition creates. An unqualified name is taken as <c>dbo</c>, the default schema a
    /// database project deploys with. A reference in a body is resolved differently, because there the
    /// caller's default schema decides (see <c>Binder</c> in Haruspex.SqlServer.Semantics).
    /// </summary>
    /// <param name="name">The name as ScriptDom parsed it. Server and database parts are ignored.</param>
    public static ObjectName From(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);
    }

    private static string Quote(string part) =>
        part.Length == 0 || part.Contains('.', StringComparison.Ordinal) || part.Contains('[', StringComparison.Ordinal) || part.Contains(']', StringComparison.Ordinal)
            ? "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]"
            : part;
}
