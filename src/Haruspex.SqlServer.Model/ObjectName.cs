namespace Haruspex.SqlServer.Model;

/// <summary>
/// An object's name, <c>schema.name</c>, and for an object of another database the database as a body names it
/// (<c>[$(Ledger)].ledger.entry</c>). Compared case-insensitively: identifiers are in a case-insensitive collation in almost every
/// SQL Server database, and treating two spellings as one object can only merge names, never invent a type.
/// </summary>
/// <param name="Schema">The schema, such as <c>dbo</c>.</param>
/// <param name="Name">The object's name within its schema.</param>
/// <param name="Database">
/// Null for an object of the database being read, which is almost every name; otherwise the other database, as a three-part name
/// in a body writes it: a name (<c>master</c>) or a SQLCMD variable (<c>$(Ledger)</c>).
/// </param>
public readonly record struct ObjectName(string Schema, string Name, string? Database = null)
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Whether every part is equal, ignoring case.</summary>
    public bool Equals(ObjectName other) =>
        Comparer.Equals(Schema, other.Schema) && Comparer.Equals(Name, other.Name) && Comparer.Equals(Database, other.Database);

    /// <summary>A hash code consistent with the case-insensitive <see cref="Equals(ObjectName)"/>.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(Comparer.GetHashCode(Schema ?? ""), Comparer.GetHashCode(Name ?? ""), Comparer.GetHashCode(Database ?? ""));

    /// <summary>
    /// The name as written in T-SQL, bracketing a part only where it must be (a dot or bracket in it), and a database's where it
    /// is a SQLCMD variable too, as a body must write one.
    /// </summary>
    public override string ToString() => DatabasePrefix + Quote(Schema) + "." + Quote(Name);

    /// <summary>The database part the name starts with, and its dot (<c>[$(Ledger)].</c>); empty for this database.</summary>
    public string DatabasePrefix => Database is null ? "" : QuoteDatabase(Database) + ".";

    private static string Quote(string part) =>
        part.Length == 0 || part.Contains('.', StringComparison.Ordinal) || part.Contains('[', StringComparison.Ordinal) || part.Contains(']', StringComparison.Ordinal)
            ? Bracketed(part)
            : part;

    private static string QuoteDatabase(string part) => part.Contains('$', StringComparison.Ordinal) ? Bracketed(part) : Quote(part);

    private static string Bracketed(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
