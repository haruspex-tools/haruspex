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
    /// A write's <c>OUTPUT</c> (or <c>OUTPUT … INTO</c>), bound as the select list of a query over the write's FROM (and a
    /// MERGE's source), filtered by its WHERE, with <c>inserted</c> and <c>deleted</c> as relations: the target after the
    /// write and before it. <c>inserted.*</c> and <c>deleted.*</c> list the target's columns.
    /// </summary>
    /// <param name="spec">The write.</param>
    /// <param name="clause">The OUTPUT or OUTPUT … INTO clause.</param>
    /// <param name="columns">Its select list.</param>
    /// <param name="from">The write's FROM (UPDATE, DELETE).</param>
    /// <param name="where">The write's WHERE (UPDATE, DELETE).</param>
    /// <param name="names">The names in force, its WITH's included.</param>
    /// <param name="inserted">The target's columns after the write, null when not known; ignored for a DELETE, which has no <c>inserted</c>.</param>
    /// <param name="deleted">The target's columns before the write, null when not known; ignored for an INSERT, which has no <c>deleted</c>.</param>
    public BoundOutput BindOutput(
        DataModificationSpecification spec,
        TSqlFragment clause,
        IList<SelectElement> columns,
        FromClause? from,
        BooleanExpression? where,
        NameEnvironment names,
        IReadOnlyList<string>? inserted,
        IReadOnlyList<string>? deleted)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(clause);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(names);
        var pseudo = new List<PseudoTable>();
        if (spec is not DeleteSpecification)
            pseudo.Add(new PseudoTable("inserted", inserted));
        if (spec is not InsertSpecification)
            pseudo.Add(new PseudoTable("deleted", deleted));
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
            {
                if (column is SelectStarExpression { Qualifier.Identifiers: [var qualifier] } star
                    && pseudo.FirstOrDefault(p => p.Name.Equals(qualifier.Value, StringComparison.OrdinalIgnoreCase)) is { Columns: { } listed })
                {
                    foreach (var name in listed)
                    {
                        var reference = Like(new ColumnReferenceExpression { MultiPartIdentifier = new MultiPartIdentifier() }, star);
                        reference.MultiPartIdentifier.Identifiers.Add(new Identifier { Value = qualifier.Value });
                        reference.MultiPartIdentifier.Identifiers.Add(new Identifier { Value = name });
                        query.SelectElements.Add(Like(new SelectScalarExpression { Expression = reference }, star));
                    }
                }
                else
                {
                    query.SelectElements.Add(column);
                }
            }

            var bound = Specification(query, null, inForce);

            // OUTPUT may name the target through its alias in FROM, but whether as the row before or after the write is not
            // observed, so the reader treats a column read through it as unknown.
            var target = from is not null && spec.Target is NamedTableReference { SchemaObject: { SchemaIdentifier: null } written }
                ? written.BaseIdentifier.Value
                : null;

            // A MERGE's source has no row for the rows its WHEN NOT MATCHED BY SOURCE clauses change.
            var unmatched = spec is MergeSpecification { TableReference: { } usingSource } merge
                && merge.ActionClauses.Any(c => c.Condition == MergeCondition.NotMatchedBySource)
                ? DmlTarget.Sources([usingSource]).Select(DmlTarget.Alias).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            return new BoundOutput(
                bound,
                [.. bound.Scope.Sources.Where(s => target is not null && target.Equals(s.Alias, StringComparison.OrdinalIgnoreCase))],
                [.. bound.Scope.Sources.Where(s => s.Alias is { } alias && unmatched.Contains(alias) && s.Target is not RelationTarget { Relation: PseudoTable })],
                pseudo);
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
