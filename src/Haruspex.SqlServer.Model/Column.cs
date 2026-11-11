using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A column as declared.</summary>
/// <param name="name">Its name.</param>
/// <param name="type">Its declared type; null for a computed column, whose type comes from its expression.</param>
/// <param name="nullability">What its declaration says about null.</param>
/// <param name="isIdentity">Whether it is an <c>IDENTITY</c> column.</param>
/// <param name="computedExpression">The expression of a computed column, or null.</param>
/// <param name="at">Where it is declared.</param>
/// <param name="collation">The collation its declaration names (<c>COLLATE Latin1_General_100_BIN2</c>), or null.</param>
public sealed class Column(string name, SqlType? type, DeclaredNullability nullability, bool isIdentity, ScalarExpression? computedExpression, SourceLocation at, string? collation = null)
{
    /// <summary>The column's name as declared.</summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The declared type, or null when none is declared (a computed column).</summary>
    public SqlType? Type { get; } = type;

    /// <summary>Whether the declaration says <c>NULL</c>, <c>NOT NULL</c> or neither.</summary>
    public DeclaredNullability Nullability { get; } = nullability;

    /// <summary>Whether it is an <c>IDENTITY</c> column.</summary>
    public bool IsIdentity { get; } = isIdentity;

    /// <summary>Whether it is a computed column.</summary>
    public bool IsComputed => ComputedExpression is not null;

    /// <summary>The expression of a computed column (<c>b AS a * 2</c>), as ScriptDom parsed it; null otherwise.</summary>
    public ScalarExpression? ComputedExpression { get; } = computedExpression;

    /// <summary>Where the column is declared.</summary>
    public SourceLocation At { get; } = at ?? throw new ArgumentNullException(nameof(at));

    /// <summary>
    /// The collation named by the column's <c>COLLATE</c> clause, or null when it names none. Without one the
    /// database's default applies, which a folder of scripts does not record.
    /// </summary>
    public string? Collation { get; } = collation;

    /// <summary>Whether it is declared <c>HIDDEN</c> (a period column of a temporal table), which <c>SELECT *</c> leaves out.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The column's name.</summary>
    public override string ToString() => Name;
}
