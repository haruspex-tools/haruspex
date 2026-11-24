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

    /// <summary>
    /// Whether a table type's column declares a default (<c>DEFAULT</c>); false for a table's, whose defaults are not read. Kept,
    /// not read as a value: a row a table-valued parameter is sent never takes it, and a column an <c>INSERT</c> doesn't list is
    /// read as any value of its type, which the default is one of.
    /// </summary>
    public bool HasDefault { get; init; }

    /// <summary>Whether it is declared <c>HIDDEN</c> (a period column of a temporal table), which <c>SELECT *</c> leaves out.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Whether it is declared <c>SPARSE</c>: <c>SELECT *</c> leaves it out when its table has a column set (<see cref="IsColumnSet"/>).</summary>
    public bool IsSparse { get; init; }

    /// <summary>
    /// Whether it is the table's column set (<c>xml COLUMN_SET FOR ALL_SPARSE_COLUMNS</c>), which stands for every sparse column
    /// in <c>SELECT *</c>: the star gives the set, as xml, in place of them.
    /// </summary>
    public bool IsColumnSet { get; init; }

    /// <summary>
    /// The masking function of a column declared <c>MASKED WITH (FUNCTION = '…')</c>, as written (<c>default()</c>,
    /// <c>partial(1,"XX",0)</c>), from its definition, an <c>ALTER COLUMN … ADD MASKED</c> that may run, or a package's column; null
    /// when it is not masked. A caller without <c>UNMASK</c> reads the function's output in place of the value, and the result of
    /// any expression over it in place of that result: which of a value's facts survive that is inference's to say
    /// (docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md).
    /// </summary>
    public string? Mask { get; init; }

    /// <summary>Whether it is masked (<see cref="Mask"/>).</summary>
    public bool IsMasked => Mask is not null;

    /// <summary>The column's name.</summary>
    public override string ToString() => Name;
}
