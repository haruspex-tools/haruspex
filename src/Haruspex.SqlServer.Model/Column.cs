using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>A column as declared.</summary>
/// <remarks>
/// A stored column and a computed one are one record, not two kinds: everything a reader asks of a column (its name,
/// nullability, collation, whether <c>SELECT *</c> shows it) is asked of both, and only where its type comes from differs. So
/// <see cref="Type"/> is null exactly when <see cref="ComputedExpression"/> is set, or when a stored column's declaration names
/// a type ScriptDom gives no name (decision 0031).
/// </remarks>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its declared type; null for a computed column, whose type comes from its expression.</param>
/// <param name="Nullability">What its declaration says about null.</param>
/// <param name="IsIdentity">Whether it is an <c>IDENTITY</c> column.</param>
/// <param name="ComputedExpression">The expression of a computed column (<c>b AS a * 2</c>), as ScriptDom parsed it; null for a stored column.</param>
/// <param name="At">Where it is declared.</param>
/// <param name="Collation">
/// The collation its <c>COLLATE</c> clause names (<c>Latin1_General_100_BIN2</c>), or null when it names none: then the
/// database's default applies, which a folder of scripts does not record.
/// </param>
public sealed record Column(string Name, SqlType? Type, DeclaredNullability Nullability, bool IsIdentity, ScalarExpression? ComputedExpression, SourceLocation At, string? Collation = null)
{
    /// <summary>Whether it is a computed column.</summary>
    public bool IsComputed => ComputedExpression is not null;

    /// <summary>Whether it is declared <c>HIDDEN</c> (a period column of a temporal table), which <c>SELECT *</c> leaves out.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The column's name.</summary>
    public override string ToString() => Name;
}
