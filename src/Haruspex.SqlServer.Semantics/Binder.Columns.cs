using Haruspex.Diagnostics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// What a column reference means: the one resolver, and a write's column list.
public sealed partial class Binder
{
    /// <summary>
    /// What a column reference means in <paramref name="scope"/>: the one rule, SQL Server's, by which every column in a body is
    /// resolved. A qualified column is the column of the source its qualifier names in the innermost scope that has one; an
    /// unqualified one is the column of the one source of the innermost scope that has a column of its name, ambiguous when
    /// more than one there has it, and of a source whose columns are not known when none is known to.
    /// </summary>
    /// <param name="column">The reference.</param>
    /// <param name="scope">The scope of the query it is read in.</param>
    public Resolution Resolve(ColumnReferenceExpression column, QueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(scope);
        var at = At(column);
        if (column.ColumnType != ColumnType.Regular || column.MultiPartIdentifier is null)
            return new UnmodelledReference(Problem(BindingProblemKind.UnsupportedReference, $"The column reference {FragmentText.OneLine(column)} is not modelled yet.", at));

        var parts = column.MultiPartIdentifier.Identifiers;
        if (parts.Count > 2)
            return new UnmodelledReference(Problem(BindingProblemKind.UnsupportedReference, $"The multi-part column reference {FragmentText.OneLine(column)} is not modelled yet.", at));

        var name = parts[^1].Value;

        // Innermost scope first: a correlated subquery sees its own sources before the outer query's.
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (parts.Count == 2)
            {
                var qualifier = parts[0].Value;
                if (s.Sources.FirstOrDefault(x => qualifier.Equals(x.Alias, StringComparison.OrdinalIgnoreCase)) is { } named)
                    return FromSource(named, name, at);
                continue;
            }

            var matches = s.Sources.Where(x => Has(x, name)).ToList();
            if (matches.Count > 1)
                return new AmbiguousColumn(Problem(BindingProblemKind.UnresolvedColumn, $"The column {name} is ambiguous: more than one table in scope has it.", at));
            if (matches.Count == 1)
                return FromSource(matches[0], name, at);

            // A source that cannot be bound, or whose columns are not known, may be the one that has this column.
            if (s.Sources.FirstOrDefault(x => x.Target is UnresolvedTarget or RelationTarget { Relation.Columns: null }) is { } unknown)
                return FromSource(unknown, name, at);
        }

        return new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, $"No table in scope has the column {FragmentText.OneLine(column)}.", at));
    }

    /// <summary>Whether a source is known to have a column of this name.</summary>
    private static bool Has(BoundSource source, string name) => source.Target switch
    {
        TableTarget { Table: var table } => table.Column(name) is not null,
        RelationTarget { Relation: var relation } => relation.IndexOf(name) >= 0,
        UnresolvedTarget => false,
    };

    private static Resolution FromSource(BoundSource source, string name, SourceLocation at) => source.Target switch
    {
        RelationTarget { Relation: { Columns: null } relation } => new UnlistedColumn(source, relation),
        RelationTarget { Relation: var relation } => relation.IndexOf(name) is >= 0 and var ordinal
            ? new RelationColumn(source, relation, ordinal)
            : new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, $"{relation.Name} has no column {name}.", at)),

        // The source's own problem, moved to the column, so that each reference is reported where it is.
        UnresolvedTarget { Problem: var problem } => new UnboundSource(source, problem with { At = at }),
        TableTarget { Table: var table } => table.Column(name) is { } column
            ? new TableColumn(source, table, column)
            : new MissingColumn(Problem(BindingProblemKind.UnresolvedColumn, $"The table {table.Name} has no column {name}.", at)),
    };

    /// <summary>
    /// The position, among a write's target's columns, of each column its column list names (<c>INSERT … (a, b)</c>,
    /// <c>OUTPUT … INTO t (a, b)</c>), by its last part, ignoring case as SQL Server does; -1 for one the target doesn't have.
    /// </summary>
    /// <param name="listed">The column list.</param>
    /// <param name="columns">The target's columns, in order.</param>
    public static IReadOnlyList<int> Written(IEnumerable<ColumnReferenceExpression> listed, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(columns);
        return [.. listed.Select(c => columns.ToList().FindIndex(n => n.Equals(WrittenColumn(c), StringComparison.OrdinalIgnoreCase)))];
    }

    /// <summary>The name of a column a write sets (<c>SET t.a = …</c>) or lists: its last part.</summary>
    /// <param name="column">The column as written.</param>
    public static string WrittenColumn(ColumnReferenceExpression column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.MultiPartIdentifier.Identifiers[^1].Value;
    }
}
