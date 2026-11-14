using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A table of another database, as a body of this one reads it through a three-part name: named with its database as the body
/// writes it (<c>[$(Ledger)].ledger.entry</c>), and carrying what is true of it there, so that every rule reading a table reads it
/// as it reads one of this database. The claim rests on the referenced package being what that database has, which the IR
/// records by the package's SHA-256 (<c>docs/decisions/0042-a-table-of-another-database-is-read-as-its-package-declares-it.md</c>).
/// </summary>
/// <remarks>
/// <para>What crosses, and why each is still true here:</para>
/// <list type="bullet">
/// <item><b>Columns, their types and nullability, identity and hidden.</b> They are the table's own. A type of that database's
/// own (one with a schema) means what it means there, and every rule here resolves a type against this database's types: so an
/// alias type is given the built-in type it stands for there, and any other (a CLR type, or a name that resolves to nothing there)
/// is named so that nothing here can resolve it, which leaves the column unknown. A package's nullability is DacFx's, an alias's
/// NOT NULL already in it. A system type (<c>sysname</c>, <c>geography</c>) is the same in every database, and stays.</item>
/// <item><b>Collation.</b> A column without <c>COLLATE</c> takes its own database's default, not this one's, so each column is
/// given its effective collation: its own, or else its database's. A database whose package records no collation is not read
/// (<see cref="AsSeenHere"/> gives null), since its strings' comparison isn't known.</item>
/// <item><b>CHECK constraints and unique keys.</b> They hold of the table's rows wherever they are read from. Their trust is
/// as that package records it.</item>
/// <item><b>Computed columns</b> cross as they are, and stay unknown, as a computed column of this database is.</item>
/// </list>
/// <para>Triggers don't cross: a write to another database's table isn't followed here, as before.</para>
/// </remarks>
public static class OtherDatabases
{
    /// <summary>The table as this database reads it, or null when its database's collation isn't known.</summary>
    /// <param name="table">The table, as its own database's model holds it.</param>
    /// <param name="other">Its database's model.</param>
    /// <param name="database">The database as the body names it.</param>
    public static Table? AsSeenHere(Table table, DatabaseModel other, string database)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(other);
        if (other.Settings?.Collation is not { } collation)
            return null;
        Column Seen(Column column) => column with { Type = Type(column.Type, other, database), Collation = column.Collation ?? collation };

        return new Table(table.Name with { Database = database }, table.Span, [.. table.Columns.Select(Seen)], table.PrimaryKey, table.Checks, table.UniqueKeys) { IsGraph = table.IsGraph };
    }

    /// <summary>
    /// A column's type as this database must read it: a type of that database's own schema resolved there, to the built-in type an
    /// alias stands for, or else qualified with the database (<c>[$(Ledger)].ledger.code</c>), which no type here has, so that it is
    /// unknown rather than taken for a type of this database of the same name. A built-in or system type is everywhere the same.
    /// </summary>
    private static SqlType? Type(SqlType? type, DatabaseModel other, string database)
    {
        if (type is not { Kind: SqlTypeKind.Named, Schema: { } schema })
            return type;
        return SchemaRules.ResolveType(other, type) is { Alias: not null } alias
            ? alias.BuiltIn
            : type with { Schema = new ObjectName(schema, type.Name, database).DatabasePrefix + schema };
    }
}
