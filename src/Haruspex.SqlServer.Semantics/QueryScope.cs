using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The scope of one query: the sources its FROM binds, in order, the enclosing query's scope beyond them (a correlated
/// subquery reads it), and the names in force. Immutable, and made only by the <see cref="Binder"/>: a compiler's symbol
/// table for one block, chained to its parent's.
/// </summary>
public sealed class QueryScope
{
    internal QueryScope(QueryScope? parent, IReadOnlyList<BoundSource> sources, NameEnvironment names)
    {
        Parent = parent;
        Sources = sources;
        Names = names;
    }

    /// <summary>The enclosing query's scope, or null for an outermost query.</summary>
    public QueryScope? Parent { get; }

    /// <summary>The sources, in the order they are written.</summary>
    public IReadOnlyList<BoundSource> Sources { get; }

    /// <summary>The relations the body has defined that names here may mean: its CTEs in scope, table variables, temporary tables.</summary>
    public NameEnvironment Names { get; }

    /// <summary>
    /// A scope with no sources of its own inside <paramref name="outer"/>, for what is read outside any FROM: <c>SET @v = …</c>,
    /// a <c>VALUES</c> row, a function's arguments.
    /// </summary>
    /// <param name="outer">The scope it reads beyond itself, or null.</param>
    /// <param name="names">The names in force, when it has no outer scope to take them from.</param>
    public static QueryScope Empty(QueryScope? outer, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return new(outer, [], outer?.Names ?? names);
    }

    /// <summary>
    /// The scope a computed column's expression is read in: the one row of its own table, read by <paramref name="row"/>, and
    /// nothing beyond it. Its expression names only that table's columns, unqualified, and reads no subquery (SQL Server refuses
    /// both, 1759 and 1046), so this is all a name in it can mean.
    /// </summary>
    /// <param name="row">The source that reads the table's row.</param>
    /// <param name="names">The names in force where the column is read.</param>
    public static QueryScope OfRow(BoundSource row, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(names);
        return new(null, [row], names);
    }

    /// <summary>The sources of this scope that a table reference binds: those of every table source in it, through its joins.</summary>
    /// <param name="reference">A table reference of this scope's FROM, or one inside it.</param>
    public IReadOnlyList<BoundSource> SourcesUnder(TableReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var leaves = new HashSet<TableReference>(ReferenceEqualityComparer.Instance);
        Collect(reference);
        return [.. Sources.Where(s => leaves.Contains(s.Syntax))];

        void Collect(TableReference r)
        {
            switch (r)
            {
                case JoinTableReference join:
                    Collect(join.FirstTableReference);
                    Collect(join.SecondTableReference);
                    break;
                case JoinParenthesisTableReference parenthesis:
                    Collect(parenthesis.Join);
                    break;
                default:
                    leaves.Add(r);
                    break;
            }
        }
    }
}
