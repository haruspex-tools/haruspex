using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A table: its columns as declared, and its primary key.</summary>
/// <remarks>
/// The model holds what the source declares and nothing it implies. Whether a column can be null, for example,
/// follows from its declaration, its key and its identity together; that interpretation belongs to whoever reads
/// the model (in haruspex, Haruspex.SqlServer.Semantics), not to the model.
/// </remarks>
public sealed class Table
{
    /// <summary>Creates a table.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="columns">Its columns, in declaration order.</param>
    /// <param name="primaryKey">The names of its primary key's columns, or none.</param>
    /// <param name="at">Where it is defined.</param>
    /// <param name="checks">Its CHECK constraints, on the table or on a column, in declaration order.</param>
    public Table(ObjectName name, IReadOnlyList<Column> columns, IReadOnlyList<string> primaryKey, SourceLocation at, IReadOnlyList<CheckConstraint>? checks = null)
    {
        Name = name;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        PrimaryKey = primaryKey ?? throw new ArgumentNullException(nameof(primaryKey));
        At = at ?? throw new ArgumentNullException(nameof(at));
        Checks = checks ?? [];
    }

    /// <summary>The CHECK constraints, whether declared on the table or on a column, in declaration order.</summary>
    public IReadOnlyList<CheckConstraint> Checks { get; }

    /// <summary>The table's name.</summary>
    public ObjectName Name { get; }

    /// <summary>The columns, in declaration order.</summary>
    public IReadOnlyList<Column> Columns { get; }

    /// <summary>
    /// The names of the primary key's columns, in key order, whether it is declared on the table or on a
    /// column. Empty when there is none.
    /// </summary>
    public IReadOnlyList<string> PrimaryKey { get; }

    /// <summary>Where the table is defined.</summary>
    public SourceLocation At { get; }

    /// <summary>The column with this name, ignoring case as SQL Server does, or null.</summary>
    public Column? Column(string name) =>
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the column is part of the primary key, ignoring case.</summary>
    public bool IsInPrimaryKey(string column) => PrimaryKey.Contains(column, StringComparer.OrdinalIgnoreCase);

    /// <summary>The table's name.</summary>
    public override string ToString() => Name.ToString();
}

/// <summary>A column as declared.</summary>
public sealed class Column
{
    /// <summary>Creates a column.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="type">Its declared type; null for a computed column, whose type comes from its expression.</param>
    /// <param name="nullability">What its declaration says about null.</param>
    /// <param name="isIdentity">Whether it is an <c>IDENTITY</c> column.</param>
    /// <param name="computedExpression">The expression of a computed column, or null.</param>
    /// <param name="at">Where it is declared.</param>
    /// <param name="collation">The collation its declaration names (<c>COLLATE Latin1_General_100_BIN2</c>), or null.</param>
    public Column(string name, SqlType? type, DeclaredNullability nullability, bool isIdentity, ScalarExpression? computedExpression, SourceLocation at, string? collation = null)
    {
        Collation = collation;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Nullability = nullability;
        IsIdentity = isIdentity;
        ComputedExpression = computedExpression;
        At = at ?? throw new ArgumentNullException(nameof(at));
    }

    /// <summary>The column's name as declared.</summary>
    public string Name { get; }

    /// <summary>The declared type, or null when none is declared (a computed column).</summary>
    public SqlType? Type { get; }

    /// <summary>Whether the declaration says <c>NULL</c>, <c>NOT NULL</c> or neither.</summary>
    public DeclaredNullability Nullability { get; }

    /// <summary>Whether it is an <c>IDENTITY</c> column.</summary>
    public bool IsIdentity { get; }

    /// <summary>Whether it is a computed column.</summary>
    public bool IsComputed => ComputedExpression is not null;

    /// <summary>The expression of a computed column (<c>b AS a * 2</c>), as ScriptDom parsed it; null otherwise.</summary>
    public ScalarExpression? ComputedExpression { get; }

    /// <summary>Where the column is declared.</summary>
    public SourceLocation At { get; }

    /// <summary>
    /// The collation named by the column's <c>COLLATE</c> clause, or null when it names none. Without one the
    /// database's default applies, which a folder of scripts does not record.
    /// </summary>
    public string? Collation { get; }

    /// <summary>The column's name.</summary>
    public override string ToString() => Name;
}

/// <summary>What a column's declaration says about null, before any key or identity is taken into account.</summary>
public enum DeclaredNullability
{
    /// <summary>
    /// Neither <c>NULL</c> nor <c>NOT NULL</c>. Then the session's ANSI_NULL_DFLT settings decide, which the
    /// source does not record.
    /// </summary>
    NotDeclared,

    /// <summary><c>NULL</c>.</summary>
    Null,

    /// <summary><c>NOT NULL</c>.</summary>
    NotNull,
}

/// <summary>A CHECK constraint as declared.</summary>
/// <remarks>
/// Only the declaration: what it implies for values (a value set, a range) is an interpretation, which belongs to
/// whoever reads the model (in haruspex, Haruspex.SqlServer.Semantics).
/// </remarks>
public sealed class CheckConstraint
{
    /// <summary>Creates a check constraint.</summary>
    /// <param name="name">Its name, or null when SQL Server is left to generate one.</param>
    /// <param name="expression">Its condition, as ScriptDom parsed it.</param>
    /// <param name="column">The column it is declared on, for a column constraint; null for a table constraint.</param>
    /// <param name="isNotForReplication">Whether it is declared <c>NOT FOR REPLICATION</c>.</param>
    /// <param name="at">Where it is declared.</param>
    /// <param name="disabledAt">Where a later statement may disable it (<c>ALTER TABLE … NOCHECK CONSTRAINT</c>), or null.</param>
    public CheckConstraint(string? name, BooleanExpression expression, string? column, bool isNotForReplication, SourceLocation at, SourceLocation? disabledAt = null)
    {
        Name = name;
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        Column = column;
        IsNotForReplication = isNotForReplication;
        At = at ?? throw new ArgumentNullException(nameof(at));
        DisabledAt = disabledAt;
    }

    /// <summary>The constraint's name, or null when none is declared.</summary>
    public string? Name { get; }

    /// <summary>The condition.</summary>
    public BooleanExpression Expression { get; }

    /// <summary>The column a column constraint is declared on; null for a table constraint.</summary>
    public string? Column { get; }

    /// <summary>
    /// Whether it is declared <c>NOT FOR REPLICATION</c>, so that rows written by a replication agent are not
    /// checked.
    /// </summary>
    public bool IsNotForReplication { get; }

    /// <summary>Where it is declared.</summary>
    public SourceLocation At { get; }

    /// <summary>
    /// Where a statement after the definition may disable it, such as <c>ALTER TABLE … NOCHECK CONSTRAINT</c>
    /// naming it or <c>ALL</c>; null when none does. Which runs last depends on the order of the scripts, which
    /// the source does not say.
    /// </summary>
    public SourceLocation? DisabledAt { get; }

    /// <summary>A copy that a later statement may disable.</summary>
    public CheckConstraint WithDisabledAt(SourceLocation at) => new(Name, Expression, Column, IsNotForReplication, At, at);

    /// <summary>The constraint's name, or where it is declared.</summary>
    public override string ToString() => Name ?? At.ToString();
}
