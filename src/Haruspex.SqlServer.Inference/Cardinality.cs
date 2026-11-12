using System.Globalization;
using Haruspex.Ir;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// How many rows a query returns: exactly one, at most one, or any number. Each rule is observed in
/// <c>RefinementObservationTests</c>, and the README beside this file says why each is sound. Anything not recognised is
/// <see cref="IrCardinality.Many"/>, which claims nothing.
/// </summary>
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// Whether the statement being read may read uncommitted: by the procedure's isolation level (FlowState), or because the
    /// view or function body being read is read so where it is used.
    /// </summary>
    private bool ReadUncommitted => BodyReadUncommitted || locals?.State.ReadUncommitted == true;

    private IrCardinality Cardinality(QuerySpecification query, BindingScope scope)
    {
        IrCardinality rows;
        if (query.GroupByClause is null && (query.HavingClause is not null || HasAggregate(query, scope)))
        {
            // An aggregate query without GROUP BY returns exactly one row, over no rows too; HAVING may filter it out
            // (observed: An_aggregate_without_group_by_returns_exactly_one_row_even_over_no_rows,
            // Having_over_no_groups_returns_no_row_and_without_group_by_at_most_one). With GROUP BY, even GROUP BY (), no
            // rows means no groups (An_aggregate_with_group_by_returns_no_row_over_no_rows).
            rows = query.HavingClause is null ? IrCardinality.One : IrCardinality.AtMostOne;
        }
        else if (query.FromClause is null && query.GroupByClause is null)
        {
            // No FROM: one row, which WHERE may filter out (A_scalar_subquery_of_an_aggregate_without_group_by_always_has_a_value_for_count).
            rows = query.WhereClause is null && query.HavingClause is null ? IrCardinality.One : IrCardinality.AtMostOne;
        }
        else if (query.GroupByClause is null && AllOneRow(query, scope))
        {
            rows = query.WhereClause is null ? IrCardinality.One : IrCardinality.AtMostOne;
        }
        else
        {
            // Grouping never makes more rows than it reads, so a filter that fixes every source's key bounds it too; but
            // ROLLUP, CUBE and GROUPING SETS add super-aggregate rows (observed:
            // Rollup_cube_and_grouping_sets_add_rows_whose_grouped_columns_are_null).
            rows = !SuperAggregate(query) && Keyed(query, scope) ? IrCardinality.AtMostOne : IrCardinality.Many;
        }

        return Limited(Limited(rows, query.TopRowFilter), query.OffsetClause);
    }

    /// <summary>
    /// TOP never adds rows. <c>TOP (0)</c> or <c>(1)</c>, without PERCENT or WITH TIES, keeps at most one; any TOP may keep
    /// none, unless it is a literal of one or more, which keeps a row that is always there (observed:
    /// Top_1_and_fetch_1_give_at_most_one_row_but_with_ties_gives_more).
    /// </summary>
    private static IrCardinality Limited(IrCardinality rows, TopRowFilter? top)
    {
        if (top is null)
            return rows;
        var n = top.Percent ? null : Literal(top.Expression);
        if (rows == IrCardinality.One)
            return n >= 1 ? IrCardinality.One : IrCardinality.AtMostOne;
        return n <= 1 && !top.WithTies ? IrCardinality.AtMostOne : rows;
    }

    /// <summary>OFFSET skips rows, so only <c>OFFSET 0</c> keeps one that is always there; FETCH of 0 or 1 keeps at most one.</summary>
    private static IrCardinality Limited(IrCardinality rows, OffsetClause? offset)
    {
        if (offset is null)
            return rows;
        var skip = Literal(offset.OffsetExpression);
        var fetch = offset.FetchExpression is null ? null : Literal(offset.FetchExpression);
        if (rows == IrCardinality.One)
            return skip == 0 && (offset.FetchExpression is null || fetch >= 1) ? IrCardinality.One : IrCardinality.AtMostOne;
        return fetch <= 1 ? IrCardinality.AtMostOne : rows;
    }

    private static long? Literal(ScalarExpression? expression) =>
        Strip(expression ?? new NullLiteral()) is IntegerLiteral literal
        && long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>
    /// Whether the select list has an aggregate of this query, which makes it an aggregate query. COUNT(*) is always
    /// this query's. An aggregate whose argument reads only an enclosing query's columns is that query's in SQL, so one
    /// counts only when every column its arguments read is of this query's own sources; an argument that reads none is not
    /// counted, which only claims less. A subquery's aggregates are its own.
    /// </summary>
    private bool HasAggregate(QuerySpecification query, BindingScope scope)
    {
        var finder = new AggregateFinder();
        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
            element.Expression.Accept(finder);
        return finder.Found.Any(call =>
        {
            if (call.Parameters is [ColumnReferenceExpression { ColumnType: ColumnType.Wildcard }])
                return true;
            var columns = new ColumnFinder();
            foreach (var parameter in call.Parameters)
                parameter.Accept(columns);
            return columns.Found.Count > 0 && columns.Found.All(c => binder.BindColumn(c, scope) switch
            {
                BoundColumn b => scope.Sources.Contains(b.Source, ReferenceEqualityComparer.Instance),
                BoundLocalColumn l => scope.Sources.Contains(l.Source, ReferenceEqualityComparer.Instance),
                _ => false,
            });
        });
    }

    /// <summary>The aggregates in an expression, not inside a subquery, and the calls <paramref name="also"/> counts as ones.</summary>
    private sealed class AggregateFinder(Func<FunctionCall, bool>? also = null) : TSqlFragmentVisitor
    {
        public List<FunctionCall> Found { get; } = [];

        public override void ExplicitVisit(ScalarSubquery node)
        {
        }

        public override void Visit(FunctionCall node)
        {
            if (IsAggregate(node) || also?.Invoke(node) == true)
                Found.Add(node);
        }
    }

    /// <summary>The column references in an expression, a subquery's included (they may read this query's columns too).</summary>
    private sealed class ColumnFinder : TSqlFragmentVisitor
    {
        public List<ColumnReferenceExpression> Found { get; } = [];

        public override void Visit(ColumnReferenceExpression node) => Found.Add(node);
    }

    // ---- keys ----------------------------------------------------------------------------------------------------------

    /// <summary>A condition that must be TRUE for a row to be kept, and the sources whose key it may fix.</summary>
    /// <param name="Condition">A conjunct of WHERE, or of a join's ON.</param>
    /// <param name="Within">The sources it may fix: any for WHERE; those of its join for ON, the optional side's for an outer join.</param>
    private sealed record Fixing(BooleanExpression Condition, HashSet<BoundSource>? Within);

    /// <summary>
    /// Whether the filters fix a whole unique key of every source, so that the query returns at most one row. A source is
    /// fixed when each column of one of its keys is compared with <c>=</c> to a value that is the same for every row of the
    /// query: a literal, a variable, an enclosing query's column, or a column of a source already fixed; or tested with
    /// <c>IS NULL</c>, since a key holds one NULL at most (observed: A_unique_constraint_allows_one_null_only). Then each
    /// combination of fixed sources' rows matches at most one row of the next, and there is one combination at most.
    /// <para>The comparison must be made in the key's own type and collation, which is what makes it the key's equality:
    /// one that converts the key can match several rows (observed: A_comparison_that_converts_the_key_can_match_several_rows,
    /// A_varchar_key_under_a_sql_collation_compared_with_nvarchar_can_match_several_rows). See <see cref="InKeysType"/>.</para>
    /// <para>Only a WHERE conjunct, or an ON conjunct of a join that fixes a source inside it, counts: a row an inner join
    /// keeps passed its ON, and an outer join matches at most one optional row per kept row when its ON fixes the optional
    /// side's key, and gives one null-extended row otherwise. Under OR nothing is fixed. Tables and views (ViewKeys) are
    /// keyed here, and a relation of at most one row is fixed already; a CTE, temporary table or table variable source, or a
    /// derived table of more rows, makes the answer many.</para>
    /// </summary>
    private bool Keyed(QuerySpecification query, BindingScope scope)
    {
        if (scope.Sources.Count == 0)
            return false;

        // A FULL JOIN is never bounded by its sides: two sides of one row each that do not match give two rows, and a key
        // tested IS NULL matches every row the other side null-extends (observed:
        // A_full_join_of_two_queries_of_at_most_one_row_can_return_two). A WHERE that fixes a key of each side by = would
        // bound it, which is not worth the care yet, so a query with one is many.
        if (query.FromClause?.TableReferences.Any(HasFullJoin) == true)
            return false;

        // Each source's keys: a table's enforced ones, a view's (ViewKeys). A relation of at most one row is fixed from the
        // start: it matches at most one row for each combination of the others' rows, and for an APPLY's right side, read for
        // each outer row, at most one for each (observed: Top_1_applied_gives_at_most_one_row_for_every_outer_row). A temporary
        // table, table variable, CTE or unresolved source makes the answer many.
        var fixedSources = new HashSet<BoundSource>(ReferenceEqualityComparer.Instance);
        var keys = new Dictionary<BoundSource, List<List<KeyColumn>>>(ReferenceEqualityComparer.Instance);
        foreach (var source in scope.Sources)
        {
            if (source.History || source.Uncommitted || ReadUncommitted)
            {
                // Read uncommitted (a NOLOCK or READUNCOMMITTED hint, or the procedure's isolation level), a scan can return a
                // row twice, so no key holds either; the same rule as history.
                // History rows repeat every key. A relation of exactly one row (an ungrouped aggregate, no FROM) stays one,
                // since its aggregate is applied over the history; one of at most one row may owe its bound to a key its
                // WHERE fixes, which the history repeats (FOR SYSTEM_TIME on a view applies to every temporal table under
                // it), so it bounds nothing here. TOP (1) would still bound it, but the cardinality does not say why.
                keys[source] = [];
                if (source.Local is { } historic && locals?.Values(historic) is { Why: null, Rows: IrCardinality.One })
                    fixedSources.Add(source);
                else if (source.Table is null && source.Local is null)
                    return false;
            }
            else if (source.Table is { } table)
            {
                keys[source] = [.. SchemaRules.EnforcedKeys(table).Select(k => k.Columns
                    .Select(c => new KeyColumn(c, -1, SchemaRules.ResolveType(model, table.Column(c)?.Type)?.Type)).ToList())];
            }
            else if (source.Local is { } local && locals?.Values(local) is { Why: null } values)
            {
                if (values.Rows != IrCardinality.Many)
                    fixedSources.Add(source);
                keys[source] = [.. values.Keys.Select(k => k.Ordinals.Zip(k.Types, (o, t) => new KeyColumn(null, o, t)).ToList())];
            }
            else
            {
                return false;
            }
        }

        var conditions = new List<Fixing>();
        if (query.WhereClause?.SearchCondition is { } where)
            conditions.AddRange(Conjuncts(where).Select(c => new Fixing(c, null)));
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            JoinConditions(reference, scope, conditions);

        bool progress;
        do
        {
            progress = false;
            foreach (var source in scope.Sources.Where(s => !fixedSources.Contains(s)))
            {
                if (keys[source].Any(key => key.All(column => conditions.Any(c => Fixes(c, source, column, scope, fixedSources)))))
                {
                    fixedSources.Add(source);
                    progress = true;
                }
            }
        }
        while (progress);

        return fixedSources.Count == scope.Sources.Count;
    }

    private static bool HasFullJoin(TableReference reference) => reference switch
    {
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter } => true,
        JoinTableReference join => HasFullJoin(join.FirstTableReference) || HasFullJoin(join.SecondTableReference),
        JoinParenthesisTableReference parenthesis => HasFullJoin(parenthesis.Join),
        _ => false,
    };

    /// <summary>
    /// Whether a FROM clause is only cross joins (or commas) of relations of exactly one row each, which is one row (observed:
    /// A_cross_join_of_two_one_row_queries_is_one_row_and_with_an_empty_side_none). An inner join's ON or an APPLY may drop it.
    /// </summary>
    private bool AllOneRow(QuerySpecification query, BindingScope scope)
    {
        static bool Crossed(TableReference reference) => reference switch
        {
            UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } cross => Crossed(cross.FirstTableReference) && Crossed(cross.SecondTableReference),
            JoinParenthesisTableReference parenthesis => Crossed(parenthesis.Join),
            JoinTableReference => false,
            _ => true,
        };

        return query.FromClause is { TableReferences: var references }
            && references.All(Crossed)
            && scope.Sources.Count > 0
            && scope.Sources.All(s => s.Local is { } local && locals?.Values(local) is { Why: null, Rows: IrCardinality.One, AtLeastOne: true });
    }

    private static void JoinConditions(TableReference reference, BindingScope scope, List<Fixing> into)
    {
        switch (reference)
        {
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join:
                JoinConditions(join.FirstTableReference, scope, into);
                JoinConditions(join.SecondTableReference, scope, into);
                into.AddRange(Conjuncts(join.SearchCondition).Select(c => new Fixing(c, Sources(join, scope))));
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join:
                JoinConditions(join.FirstTableReference, scope, into);
                JoinConditions(join.SecondTableReference, scope, into);
                into.AddRange(Conjuncts(join.SearchCondition).Select(c => new Fixing(c, Sources(join.SecondTableReference, scope))));
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } cross:
                JoinConditions(cross.FirstTableReference, scope, into);
                JoinConditions(cross.SecondTableReference, scope, into);
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply } apply:
                // No ON of its own; the joins on either side keep theirs. An OUTER APPLY's right side gives one null-extended
                // row when it has none, so one that matches at most one row per outer row keeps the bound, as a left join's.
                JoinConditions(apply.FirstTableReference, scope, into);
                JoinConditions(apply.SecondTableReference, scope, into);
                break;
            case JoinParenthesisTableReference parenthesis:
                JoinConditions(parenthesis.Join, scope, into);
                break;
        }
    }

    /// <summary>A column of a source's key: a table's by name, a relation's by position; with its built-in type, when known.</summary>
    private sealed record KeyColumn(string? Name, int Ordinal, BuiltInType? Type);

    /// <summary>The conjuncts of a condition joined by AND, through parentheses.</summary>
    private static IEnumerable<BooleanExpression> Conjuncts(BooleanExpression condition) => condition switch
    {
        BooleanParenthesisExpression p => Conjuncts(p.Expression),
        BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and => Conjuncts(and.FirstExpression).Concat(Conjuncts(and.SecondExpression)),
        _ => [condition],
    };

    /// <summary>Whether a condition fixes this column of this source, given the sources already fixed.</summary>
    private bool Fixes(Fixing fixing, BoundSource source, KeyColumn column, BindingScope scope, HashSet<BoundSource> fixedSources)
    {
        if (fixing.Within is { } within && !within.Contains(source))
            return false;

        // A view's key column is its table's, unchanged (ViewKeys), so it compares in that column's type and collation.
        bool IsKeyColumn(ScalarExpression e) =>
            Strip(e) is ColumnReferenceExpression { Collation: null } reference
            && binder.BindColumn(reference, scope) switch
            {
                BoundColumn bound => ReferenceEquals(bound.Source, source) && column.Name is { } name && bound.Column.Name.Equals(name, StringComparison.OrdinalIgnoreCase),
                BoundLocalColumn local => ReferenceEquals(local.Source, source) && column.Name is null && local.Ordinal == column.Ordinal,
                _ => false,
            };

        switch (fixing.Condition)
        {
            case BooleanIsNullExpression { IsNot: false } isNull:
                return IsKeyColumn(isNull.Expression);
            case BooleanComparisonExpression { ComparisonType: BooleanComparisonType.Equals } equals:
            {
                ScalarExpression other;
                if (IsKeyColumn(equals.FirstExpression))
                    other = equals.SecondExpression;
                else if (IsKeyColumn(equals.SecondExpression))
                    other = equals.FirstExpression;
                else
                    return false;

                return column.Type is { } keyType
                    && ValueType(Strip(other), source, scope, fixedSources) is { } valueType
                    && InKeysType(keyType, valueType);
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// The built-in type of a value that is the same for every row of the query, with no COLLATE of its own; null when it
    /// is not such a value, or its type is not known. A null literal is <see cref="KeyValue.Null"/>: it matches no row.
    /// </summary>
    private KeyValue? ValueType(ScalarExpression value, BoundSource keySource, BindingScope scope, HashSet<BoundSource> fixedSources)
    {
        return value switch
        {
            PrimaryExpression { Collation: not null } => null,
            NullLiteral => KeyValue.Null,
            IntegerLiteral literal => new KeyValue(long.TryParse(literal.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= int.MaxValue ? BuiltInType.Int : BuiltInType.Numeric),
            NumericLiteral => new KeyValue(BuiltInType.Numeric),
            StringLiteral literal => new KeyValue(literal.IsNational ? BuiltInType.NVarChar : BuiltInType.VarChar),
            VariableReference variable => variableTypes.TryGetValue(variable.Name, out var declared)
                ? KeyValue.Of(SchemaRules.ResolveType(model, declared)?.Type)
                : null,
            ColumnReferenceExpression reference => binder.BindColumn(reference, scope) switch
            {
                // A column of an enclosing query is one value while this query runs; one of this query is, once its
                // source is fixed. Never the key's own source: t.id = t.n fixes nothing.
                BoundColumn bound when !ReferenceEquals(bound.Source, keySource)
                    && (!scope.Sources.Contains(bound.Source, ReferenceEqualityComparer.Instance) || fixedSources.Contains(bound.Source))
                    => KeyValue.Of(SchemaRules.ResolveType(model, bound.Column.Type)?.Type),
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>A value compared with a key: of a built-in type, or the NULL literal (<see cref="Type"/> null).</summary>
    private sealed record KeyValue(BuiltInType? Type)
    {
        public static KeyValue Null { get; } = new((BuiltInType?)null);

        public static KeyValue? Of(BuiltInType? type) => type is null ? null : new KeyValue(type);
    }

    /// <summary>
    /// Whether <c>key = value</c> compares in the key's own terms, so that it matches only rows whose key equals one value,
    /// of which a unique key has one at most. SQL Server converts the side of lower precedence to the other's type, and
    /// compares strings under the key's collation when the value is a literal or variable (its collation is the database's
    /// default, and the column's wins) or a column (a different one is an error, 468). So it is enough that the key is
    /// not converted, or only widened without merging values:
    /// <list type="bullet">
    /// <item>the same type, or both integers, or both exact decimals, or an integer with a decimal (each widens exactly);</item>
    /// <item>varchar or char with varchar or char;</item>
    /// <item>nvarchar or nchar with any of the four string types: a varchar value becomes nvarchar, the key is unchanged;</item>
    /// <item>a key of any type but a string or binary type with a string value, which is converted to the key's type.</item>
    /// </list>
    /// A varchar key with an nvarchar value is not: the key becomes nvarchar, compared by Unicode rules, under which a SQL
    /// collation's distinct varchar values can be equal (observed). Nor is a string key with a number (observed). sql_variant
    /// compares across types, and is left out.
    /// </summary>
    private static bool InKeysType(BuiltInType key, KeyValue value)
    {
        if (value.Type is not { } type)
            return true;
        if (key == BuiltInType.SqlVariant || type == BuiltInType.SqlVariant)
            return false;
        if (key == type)
            return true;
        static bool Exact(BuiltInType t) => t.Family is TypeFamily.Integer or TypeFamily.Decimal;
        static bool Text(BuiltInType t) => t.Family == TypeFamily.String;
        if (Exact(key) && Exact(type))
            return true;
        if (Text(key) && Text(type) && (key.IsNational || !type.IsNational))
            return true;
        return !Text(key) && key.Family != TypeFamily.Binary && Text(type);
    }
}
