using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// The scopes a write's SET values and OUTPUT are read in.
public sealed partial class Binder
{
    /// <summary>
    /// The scope an <c>UPDATE</c>'s <c>SET</c> values are read in: a query over its FROM, or over its target alone when it has
    /// none, filtered by its WHERE.
    /// </summary>
    /// <param name="update">The update.</param>
    /// <param name="names">The names in force, its WITH's included.</param>
    public BoundSpecification BindUpdate(UpdateSpecification update, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(names);
        return bindings.Once(update, null, names, () =>
        {
            var from = update.FromClause ?? new FromClause { TableReferences = { update.Target } };
            return Specification(new QuerySpecification { FromClause = from, WhereClause = update.WhereClause }, null, names);
        });
    }

    /// <summary>
    /// The scope a <c>MERGE</c>'s actions read their values in: its target and its source, each row as it is, with no
    /// condition. An action reads only the rows it has: a column of the target in <c>WHEN NOT MATCHED [BY TARGET]</c>, or of
    /// the source in <c>WHEN NOT MATCHED BY SOURCE</c>, is refused (observed: A_merge_action_reads_only_the_rows_it_has), so
    /// a column read is never of a row the action lacks. What <c>ON</c> and each <c>WHEN … AND</c> prove isn't read, which
    /// only claims less. The target is named by its alias, which ScriptDom keeps on the merge rather than on the target
    /// (<see cref="MergeSpecification.TableAlias"/>; observed: A_merge_action_reads_the_target_through_its_alias).
    /// </summary>
    /// <param name="merge">The merge.</param>
    /// <param name="names">The names in force, its WITH's included.</param>
    public BoundSpecification BindMerge(MergeSpecification merge, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(merge);
        ArgumentNullException.ThrowIfNull(names);
        return bindings.Once(merge, null, names, () =>
            Specification(new QuerySpecification { FromClause = new FromClause { TableReferences = { Aliased(merge.Target, merge.TableAlias), merge.TableReference } } }, null, names));
    }

    /// <summary>A MERGE's target with its alias on it, as a FROM source carries one: a table's or a table variable's.</summary>
    private static TableReference Aliased(TableReference target, Identifier? alias) => (target, alias) switch
    {
        (NamedTableReference { Alias: null } named, { }) => Like(new NamedTableReference { SchemaObject = named.SchemaObject, Alias = alias }, named),
        (VariableTableReference { Alias: null } variable, { }) => Like(new VariableTableReference { Variable = variable.Variable, Alias = alias }, variable),
        _ => target,
    };

    /// <summary>
    /// A write's <c>OUTPUT</c> (or <c>OUTPUT … INTO</c>), bound as the select list of a query over the write's FROM (and a
    /// MERGE's source), filtered by its WHERE, with <c>inserted</c> and <c>deleted</c> as relations: the target after the
    /// write and before it. <c>inserted.*</c> and <c>deleted.*</c> are stars as any other (<see cref="Star"/>), so they give the
    /// columns a star over the target gives (observed: Output_stars_leave_out_what_a_star_over_the_target_leaves_out).
    /// </summary>
    /// <param name="spec">The write.</param>
    /// <param name="clause">The OUTPUT or OUTPUT … INTO clause.</param>
    /// <param name="columns">Its select list.</param>
    /// <param name="from">The write's FROM (UPDATE, DELETE).</param>
    /// <param name="where">The write's WHERE (UPDATE, DELETE).</param>
    /// <param name="names">The names in force, its WITH's included.</param>
    /// <param name="inserted">The target's columns after the write, null when not known; ignored for a DELETE, which has no <c>inserted</c>.</param>
    /// <param name="deleted">The target's columns before the write, null when not known; ignored for an INSERT, which has no <c>deleted</c>.</param>
    /// <param name="written">The table written, when it is a table of the schema or a table variable (<see cref="PseudoTable.Written"/>).</param>
    public BoundOutput BindOutput(
        DataModificationSpecification spec,
        TSqlFragment clause,
        IList<SelectElement> columns,
        FromClause? from,
        BooleanExpression? where,
        NameEnvironment names,
        EquatableArray<string>? inserted,
        EquatableArray<string>? deleted,
        ITable? written)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(names);
        var pseudo = new List<PseudoTable>();
        if (spec is not DeleteSpecification)
            pseudo.Add(new PseudoTable("inserted", inserted) { Written = written });
        if (spec is not InsertSpecification)
            pseudo.Add(new PseudoTable("deleted", deleted) { Written = written });
        var inForce = pseudo.Aggregate(names, (n, p) => n.WithPseudo(p));

        return bindings.Once(clause, null, inForce, () =>
        {
            var query = new QuerySpecification { FromClause = new FromClause(), WhereClause = where is null ? null : new WhereClause { SearchCondition = where } };
            foreach (var source in from?.TableReferences ?? [])
                query.FromClause.TableReferences.Add(source);
            if (spec is MergeSpecification { TableReference: { } merged })
                query.FromClause.TableReferences.Add(merged);
            foreach (var table in pseudo)
                query.FromClause.TableReferences.Add(Like(new NamedTableReference { SchemaObject = OnePart(table.Name) }, columns[0]));

            foreach (var column in columns)
                query.SelectElements.Add(column);

            var bound = Specification(query, null, inForce);

            // OUTPUT may name the target through its alias in FROM, but whether as the row before or after the write is not
            // observed, so the reader treats a column read through it as unknown. A target named with its schema is the FROM
            // source of that full name without an alias, which OUTPUT can't name at all (observed:
            // Output_cannot_name_the_target_by_its_table_name), so a column read through it is unknown too.
            bool Written(BoundSource source) => from is not null && spec.Target switch
            {
                NamedTableReference { SchemaObject: { SchemaIdentifier: null } one } => one.BaseIdentifier.Value.Equals(source.Alias, StringComparison.OrdinalIgnoreCase),
                NamedTableReference { SchemaObject: { DatabaseIdentifier: null, SchemaIdentifier.Value: var schema, BaseIdentifier.Value: var name } } =>
                    source.Exposed?.IsNamed(schema, name) == true,
                _ => false,
            };

            // A MERGE's source has no row for the rows its WHEN NOT MATCHED BY SOURCE clauses change.
            var unmatched = spec is MergeSpecification { TableReference: { } usingSource } merge
                && merge.ActionClauses.Any(c => c.Condition == MergeCondition.NotMatchedBySource)
                ? DmlTarget.Sources([usingSource]).Select(DmlTarget.Alias).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            return new BoundOutput(
                bound,
                [.. bound.Scope.Sources.Where(Written)],
                [.. bound.Scope.Sources.Where(s => s.Alias is { } alias && unmatched.Contains(alias) && s.Target is not RelationTarget { Relation: PseudoTable })],
                [.. pseudo]);
        });
    }

    private static SchemaObjectName OnePart(string name)
    {
        var schemaObject = new SchemaObjectName();
        schemaObject.Identifiers.Add(new Identifier { Value = name });
        return schemaObject;
    }

    /// <summary>A fragment made here, placed where <paramref name="like"/> is, so that locations and texts name the source.</summary>
    private static T Like<T>(T fragment, TSqlFragment like)
        where T : TSqlFragment
    {
        fragment.ScriptTokenStream = like.ScriptTokenStream;
        fragment.FirstTokenIndex = like.FirstTokenIndex;
        fragment.LastTokenIndex = like.LastTokenIndex;
        return fragment;
    }
}
