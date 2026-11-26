using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// The one table a write through a CTE or a derived table changes, the queue-claim idiom (WITH next AS (SELECT TOP (1) …)
// UPDATE next … OUTPUT inserted.…). The Inference README ("OUTPUT") gives the model; the observations it rests on are in
// DmlObservationTests' OutputThrough part.
public sealed partial class Binder
{
    /// <summary>
    /// The table a write through a CTE or a derived table changes, read from the source as written, or why it isn't known. The
    /// server names <c>inserted</c> and <c>deleted</c>'s columns as the source does, lists only those for a star, and types each
    /// as the table declares it (observed:
    /// Output_through_a_cte_or_derived_table_is_the_columns_it_names_typed_as_the_table_declares_them). Only a source the
    /// server takes and whose every column is one of the table's, unchanged, is followed: one query of one source, with no
    /// join, <c>APPLY</c>, aggregate, <c>DISTINCT</c> or set operation, which the server refuses or which may write another
    /// table (Writes_through_a_cte_of_a_join_an_aggregate_distinct_or_a_set_operation_are_refused_or_reach_another_table), and
    /// no computed column, which the server allows as its expression over the row but which is not modelled.
    /// </summary>
    /// <param name="spec">The write, whose target is a CTE of <paramref name="with"/> or the alias of a source in <paramref name="from"/>.</param>
    /// <param name="from">Its FROM.</param>
    /// <param name="with">Its WITH.</param>
    public WrittenThrough WriteThrough(DataModificationSpecification spec, FromClause? from, WithCtesAndXmlNamespaces? with)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var ctes = with?.CommonTableExpressions.ToList() ?? [];
        return Reference(spec.Target, aliases: true, ctes.Count);

        // A source on the way down: a CTE (each sees only the CTEs before it), a derived table, or the table at the bottom.
        WrittenThrough Reference(TableReference reference, bool aliases, int visible)
        {
            switch (reference)
            {
                case NamedTableReference { SchemaObject: { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null } name } named:
                    var one = name.BaseIdentifier.Value;

                    // UPDATE p … FROM (SELECT …) AS p, or UPDATE c … FROM c JOIN …: the target is the source of that alias.
                    if (aliases && from is not null
                        && DmlTarget.Sources(from.TableReferences).FirstOrDefault(s => one.Equals(DmlTarget.Alias(s), StringComparison.OrdinalIgnoreCase)) is { } aliased
                        && !ReferenceEquals(aliased, named))
                    {
                        return Reference(aliased, aliases: false, visible);
                    }

                    var index = ctes.FindIndex(0, visible, c => c.ExpressionName.Value.Equals(one, StringComparison.OrdinalIgnoreCase));
                    return index >= 0 ? Query(ctes[index].QueryExpression, ctes[index].Columns, index, $"the CTE {one}") : Underneath(named);
                case NamedTableReference named:
                    return Underneath(named);
                case QueryDerivedTable derived:
                    return Query(derived.QueryExpression, derived.Columns, visible, $"the derived table {derived.Alias?.Value}");
                case InlineDerivedTable:
                    return WrittenThrough.Not("its source is a VALUES list, which is no table");
                case VariableTableReference variable:
                    return WrittenThrough.Not($"its source reads the table variable {variable.Variable.Name}, which is not followed");
                case JoinTableReference or JoinParenthesisTableReference:
                    return WrittenThrough.Not($"its source joins {FragmentText.OneLine(reference)}, so more than one table may be written");
                default:
                    return WrittenThrough.Not($"its source reads {FragmentText.OneLine(reference)}, which is not one table");
            }
        }

        // The table at the bottom: one table of the project, defined once, whose star lists every column it declares.
        WrittenThrough Underneath(NamedTableReference named)
        {
            var text = FragmentText.OneLine(named.SchemaObject);
            if (named.TemporalClause is not null || named.TableSampleClause is not null)
                return WrittenThrough.Not($"its source reads {text} FOR SYSTEM_TIME or TABLESAMPLE, which is not followed");
            if (Temporary(named.SchemaObject) is { } temporary)
                return WrittenThrough.Not($"its source reads the temporary table {temporary}, which is not followed");
            if (ResolveWritten(named.SchemaObject) is not [var name] || Model.FindTableRedefinition(name) is not null || Model.FindTable(name) is not { } table)
                return WrittenThrough.Not($"its source reads {text}, which is not one table of the project (a view, a synonym, a table defined elsewhere or more than one)");
            if (table.IsGraph || table.Columns.Any(c => c.IsHidden || c.IsColumnSet))
                return WrittenThrough.Not($"{text} has columns a star leaves out or adds, which are not followed through a CTE");
            return new WrittenThrough(table, [.. table.Columns.Select(c => new WrittenColumn(c.Name, c))], [], null);
        }

        // A CTE's or a derived table's query: one source, each column one of that source's, unchanged.
        WrittenThrough Query(QueryExpression expression, IList<Identifier> renamed, int visible, string what)
        {
            while (expression is QueryParenthesisExpression parenthesis)
                expression = parenthesis.QueryExpression;
            if (expression is not QuerySpecification query)
                return WrittenThrough.Not($"{what} is a set operation, which the server does not write through");
            if (query.UniqueRowFilter == UniqueRowFilter.Distinct)
                return WrittenThrough.Not($"{what} has DISTINCT, which the server does not write through");
            if (query.GroupByClause is not null || query.HavingClause is not null)
                return WrittenThrough.Not($"{what} has GROUP BY or HAVING, which the server does not write through");
            if (query.ForClause is not null)
                return WrittenThrough.Not($"{what} has a FOR clause, which is not modelled");
            if (query.FromClause?.TableReferences is not [var only])
                return WrittenThrough.Not($"{what} reads more than one source, or none, so it is not one table");

            var below = Reference(only, aliases: false, visible);
            if (below.Why is not null)
                return below;

            var columns = new List<WrittenColumn>();
            foreach (var element in query.SelectElements)
            {
                switch (element)
                {
                    case SelectStarExpression star when QualifiesTheSource(star.Qualifier?.Identifiers.ToList() ?? [], only):
                        columns.AddRange(below.Columns);
                        break;

                    // A column with COLLATE is refused (observed, 4406), so it is not one of the table's unchanged.
                    case SelectScalarExpression { Expression: var value, ColumnName: var alias }
                        when WithoutParentheses(value) is ColumnReferenceExpression { ColumnType: ColumnType.Regular, Collation: null, MultiPartIdentifier.Identifiers: { Count: > 0 } parts }
                            && QualifiesTheSource([.. parts.Take(parts.Count - 1)], only):
                        var last = parts[parts.Count - 1].Value;
                        if (below.Columns.Where(c => c.Name.Equals(last, StringComparison.OrdinalIgnoreCase)).ToList() is not [var column])
                            return WrittenThrough.Not($"{what} names {last}, which is not one column of its source");
                        columns.Add(column with { Name = alias?.Value ?? last });
                        break;
                    default:
                        return WrittenThrough.Not($"{what} has a computed column, {FragmentText.OneLine(element)}, which is not modelled");
                }
            }

            // A column list renames the columns in order; the server refuses a list of another length.
            if (renamed.Count > 0)
            {
                if (renamed.Count != columns.Count)
                    return WrittenThrough.Not($"{what} names {renamed.Count} columns for {columns.Count}");
                columns = [.. columns.Select((c, i) => c with { Name = renamed[i].Value })];
            }

            if (columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return WrittenThrough.Not($"{what} gives two columns one name");
            return below with { Columns = [.. columns], Queries = [query, .. below.Queries] };
        }
    }

    private static ScalarExpression WithoutParentheses(ScalarExpression expression)
    {
        while (expression is ParenthesisExpression { Collation: null } parenthesis)
            expression = parenthesis.Expression;
        return expression;
    }

    /// <summary>
    /// Whether a column's or a star's qualifier names the one source of its query: none, its alias, or, for a table without
    /// one, its name or its schema and name.
    /// </summary>
    private static bool QualifiesTheSource(IReadOnlyList<Identifier> qualifier, TableReference source) => (qualifier, source) switch
    {
        ([], _) => true,
        ([var one], TableReferenceWithAlias { Alias: { } alias }) => one.Value.Equals(alias.Value, StringComparison.OrdinalIgnoreCase),
        (_, TableReferenceWithAlias { Alias: not null }) => false,
        ([var one], NamedTableReference { SchemaObject.BaseIdentifier.Value: var name }) => one.Value.Equals(name, StringComparison.OrdinalIgnoreCase),
        ([var schema, var one], NamedTableReference { SchemaObject: { DatabaseIdentifier: null, SchemaIdentifier.Value: var owner, BaseIdentifier.Value: var name } }) =>
            schema.Value.Equals(owner, StringComparison.OrdinalIgnoreCase) && one.Value.Equals(name, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
