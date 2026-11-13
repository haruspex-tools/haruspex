using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Binds the names in one body to what they mean: each query's FROM into a scope of sources (tables of the schema, the
/// body's own relations, views and calls, each a <see cref="RelationSymbol"/>), with derived tables, <c>APPLY</c> sides and
/// CTEs bound recursively into a tree (<see cref="BoundQuery"/>); each column reference to the column it reads
/// (<see cref="Resolve"/>); each <c>EXEC</c> to the procedure it runs. Every reference it cannot bind for certain becomes a
/// <see cref="BindingProblem"/>, never a guess.
/// </summary>
/// <remarks>
/// <para>Binding is a function of a fragment, the scope it is read inside and the names in force, and nothing else: no value,
/// no path. So each query is bound once for each set of names it is read with, the first time it is read, and kept
/// (<see cref="Bindings"/>), as a compiler's semantic model binds a node on demand and keeps it. A statement read on every
/// pass of a loop, or in both arms of a branch, with the same names is bound once.</para>
/// <para>The names in force (<see cref="NameEnvironment"/>) are an argument, not something binding asks for: what a
/// temporary table's columns are at a statement depends on the path to it, which is the walk's to know.</para>
/// </remarks>
public sealed class Binder
{
    private readonly Bindings bindings;
    private readonly SourceSpan source;

    /// <summary>A binder of one body, keeping what it binds for itself alone.</summary>
    /// <param name="model">The schema the body runs against.</param>
    /// <param name="source">Where the object whose body it is was read; positions in the body are in its file.</param>
    public Binder(DatabaseModel model, SourceSpan source)
        : this(new Bindings(model), source)
    {
    }

    internal Binder(Bindings bindings, SourceSpan source)
    {
        this.bindings = bindings;
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private DatabaseModel Model => bindings.Model;

    // ---- queries ------------------------------------------------------------------------------------------------------

    /// <summary>Binds a query expression read inside <paramref name="outer"/>: a statement's query, a subquery, a CTE's.</summary>
    /// <param name="query">The query.</param>
    /// <param name="outer">The enclosing query's scope, for a subquery; null for an outermost query.</param>
    /// <param name="names">The names in force, for an outermost query; a subquery takes its enclosing scope's.</param>
    public BoundQuery Bind(QueryExpression query, QueryScope? outer, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(names);
        var inForce = outer?.Names ?? names;
        return bindings.Once<BoundQuery>(query, outer, inForce, () => query switch
        {
            QuerySpecification specification => Specification(specification, outer, inForce),
            BinaryQueryExpression operation => new BoundSetOperation(operation, Bind(operation.FirstQueryExpression, outer, inForce), Bind(operation.SecondQueryExpression, outer, inForce)),
            QueryParenthesisExpression parenthesis => new BoundParenthesis(parenthesis, Bind(parenthesis.QueryExpression, outer, inForce)),
            _ => throw new ArgumentException($"{query.GetType().Name} is not a query expression the parser gives.", nameof(query)),
        });
    }

    /// <summary>
    /// Binds a statement's <c>WITH</c>: each CTE in order, so each may read the ones before it, a recursive one (one whose query
    /// reads its own name) with itself in scope. The names it gives are in force for the rest of the statement only.
    /// </summary>
    /// <param name="with">The statement's WITH, or null for none.</param>
    /// <param name="names">The names in force before it.</param>
    public BoundWith BindWith(WithCtesAndXmlNamespaces? with, NameEnvironment names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (with is null)
            return new BoundWith(names, []);
        return bindings.Once(with, null, names, () =>
        {
            var inForce = names;
            var ctes = new List<BoundCommonTable>();
            foreach (var cte in with.CommonTableExpressions)
            {
                var name = cte.ExpressionName.Value;
                var columnList = cte.Columns.Select(c => c.Value).ToList();
                CommonTable symbol;
                BoundQuery query;
                if (DmlTarget.NamesRead(cte.QueryExpression).Any(n => IsOnePart(n) && n.BaseIdentifier.Value.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    // Its columns are its anchor's, the first query of its UNION ALL, read before the CTE is in scope.
                    var anchor = Bind(FirstQuery(cte.QueryExpression), null, inForce);
                    symbol = new CommonTable(cte, anchor.Listed(columnList), IsRecursive: true);
                    query = Bind(cte.QueryExpression, null, inForce.WithCte(symbol));
                }
                else
                {
                    query = Bind(cte.QueryExpression, null, inForce);
                    symbol = new CommonTable(cte, query.Listed(columnList), IsRecursive: false);
                }

                ctes.Add(new BoundCommonTable(symbol, query));
                inForce = inForce.WithCte(symbol);
            }

            return new BoundWith(inForce, ctes);
        });
    }

    /// <summary>A query expression's first query specification, through set operations and parentheses: a recursive CTE's anchor.</summary>
    private static QueryExpression FirstQuery(QueryExpression query) => query switch
    {
        BinaryQueryExpression operation => FirstQuery(operation.FirstQueryExpression),
        _ => query,
    };

    private static bool IsOnePart(SchemaObjectName name) =>
        name is { SchemaIdentifier: null, DatabaseIdentifier: null, ServerIdentifier: null };

    private BoundSpecification Specification(QuerySpecification query, QueryScope? outer, NameEnvironment names)
    {
        var sources = new List<BoundSource>();
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            AddSources(new Adding(sources, outer, names), reference, optionalSideOf: null);
        var scope = new QueryScope(outer, sources, names);
        return new BoundSpecification(query, scope, Names(query, scope));
    }

    /// <summary>
    /// The name SQL Server gives a select-list column: its alias, <c>$action</c> for MERGE's action (observed:
    /// Merge_action_is_an_nvarchar_10), or a column reference's last part; null for any other expression without an alias.
    /// </summary>
    /// <param name="column">The column.</param>
    public static string? ColumnName(SelectScalarExpression column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return column.ColumnName?.Value
            ?? (column.Expression is ColumnReferenceExpression { ColumnType: ColumnType.PseudoColumnAction } ? "$action" : null)
            ?? (column.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers[^1].Value;
    }

    /// <summary>
    /// A query's columns' names (<see cref="ColumnName"/>); a star's columns when stars are expanded; null when a star is not.
    /// </summary>
    private static List<string?>? Names(QuerySpecification query, QueryScope scope)
    {
        var names = new List<string?>();
        foreach (var element in query.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    names.Add(ColumnName(scalar));
                    break;
                case SelectStarExpression star when scope.Names.ExpandsStars && Star(star, scope) is { } columns:
                    names.AddRange(columns.Select(c => c.Name));
                    break;
                case SelectStarExpression:
                    return null;
            }
        }

        return names;
    }

    // ---- writes -------------------------------------------------------------------------------------------------------

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

    // ---- SELECT * -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The columns <c>*</c> or <c>alias.*</c> stands for, each resolved: each source's columns in order, a table's as declared,
    /// a relation's as it names them. Null when one of them is not known, or a table's <c>*</c> differs from its declared
    /// columns: a hidden period column is left out of it, and a graph table adds pseudo-columns.
    /// </summary>
    /// <param name="star">The star.</param>
    /// <param name="scope">The scope of the query whose select list it is in.</param>
    public static IReadOnlyList<StarColumn>? Star(SelectStarExpression star, QueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(star);
        ArgumentNullException.ThrowIfNull(scope);
        IEnumerable<BoundSource> sources = scope.Sources;
        if (star.Qualifier is { } qualifier)
        {
            if (qualifier.Identifiers.Count != 1)
                return null;
            var named = scope.Sources.Where(s => qualifier.Identifiers[0].Value.Equals(s.Alias, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count != 1)
                return null;
            sources = named;
        }

        var columns = new List<StarColumn>();
        foreach (var source in sources)
        {
            switch (source.Target)
            {
                case TableTarget { Table: var table } when !table.IsGraph && !table.Columns.Any(c => c.IsHidden):
                    columns.AddRange(table.Columns.Select(c => new StarColumn(c.Name, new TableColumn(source, table, c))));
                    break;
                case RelationTarget { Relation: { Columns: { } names } relation }:
                    columns.AddRange(names.Select((n, i) => new StarColumn(n, new RelationColumn(source, relation, i))));
                    break;
                case TableTarget:
                case RelationTarget:
                case UnresolvedTarget:
                    return null;
            }
        }

        return columns;
    }

    // ---- FROM ---------------------------------------------------------------------------------------------------------

    /// <summary>The sources being added to one query's scope, the scope beyond it, and the names in force.</summary>
    private sealed record Adding(List<BoundSource> Sources, QueryScope? Parent, NameEnvironment Names)
    {
        /// <summary>
        /// Inside an APPLY's right side, the scope it reads outer columns from (its left input's sources, then the enclosing
        /// query's); null elsewhere, where a derived table reads only the enclosing query's.
        /// </summary>
        public QueryScope? Lateral { get; init; }

        /// <summary>How the optional side being added to came about (see <see cref="Side"/>).</summary>
        public Side Side { get; init; } = Side.Join;

        /// <summary>The scope a derived table, <c>VALUES</c> list or call here reads beyond itself.</summary>
        public QueryScope? Outer => Lateral ?? Parent;
    }

    /// <summary>What kind of optional side a source is added on.</summary>
    private enum Side
    {
        /// <summary>None, or an outer join's.</summary>
        Join,

        /// <summary>An OUTER APPLY's right side, which the source is the whole of.</summary>
        Applied,

        /// <summary>An OUTER APPLY's right side, which the source is joined inside.</summary>
        InsideApplied,
    }

    /// <param name="adding">The scope being built, and where in its FROM the reference is.</param>
    /// <param name="reference">The table reference.</param>
    /// <param name="optionalSideOf">The outermost optional side it is inside, if any.</param>
    private void AddSources(Adding adding, TableReference reference, SourceLocation? optionalSideOf)
    {
        // A source joined inside an OUTER APPLY's right side is on that side, but is not the whole of it.
        var inside = adding with { Side = adding.Side == Side.Applied ? Side.InsideApplied : adding.Side };
        var asJoin = inside with { Side = Side.Join };

        switch (reference)
        {
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join:
                AddSources(inside, join.FirstTableReference, optionalSideOf);
                AddSources(inside, join.SecondTableReference, optionalSideOf);
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join:
                // The right side has no row when nothing matches. Recorded on each of its sources, so a reader
                // can tell that any column read from them may have no value, whatever its declaration says.
                AddSources(inside, join.FirstTableReference, optionalSideOf);
                AddSources(optionalSideOf is null ? asJoin : inside, join.SecondTableReference, optionalSideOf ?? At(join.SecondTableReference));
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply } apply:
            {
                // The right side is read once for each row of the left input, and may read its columns; not those of a
                // source listed before it with a comma, which is not part of its left input (observed:
                // An_applied_query_sees_its_left_input_only). OUTER APPLY null-extends an outer row the right side gives
                // no row for, as a left join does; CROSS APPLY drops it, so its right side is optional only as the APPLY is.
                var first = adding.Sources.Count;
                AddSources(inside, apply.FirstTableReference, optionalSideOf);
                var left = new QueryScope(adding.Outer, [.. adding.Sources.Skip(first)], adding.Names);
                if (apply.UnqualifiedJoinType == UnqualifiedJoinType.OuterApply && optionalSideOf is null)
                    AddSources(adding with { Lateral = left, Side = Side.Applied }, apply.SecondTableReference, At(apply.SecondTableReference));
                else
                    AddSources(inside with { Lateral = left }, apply.SecondTableReference, optionalSideOf);
                break;
            }

            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter } join:
                // Each side has no row where nothing on the other matches, so both are optional, each at its own location
                // (observed: A_full_outer_join_null_extends_both_sides_where_nothing_matches). Inside an outer join's optional
                // side the enclosing one is kept, as for nested left joins.
                AddSources(optionalSideOf is null ? asJoin : inside, join.FirstTableReference, optionalSideOf ?? At(join.FirstTableReference));
                AddSources(optionalSideOf is null ? asJoin : inside, join.SecondTableReference, optionalSideOf ?? At(join.SecondTableReference));
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } cross:
                // Every row of one with every row of the other: both sides read as they are.
                AddSources(inside, cross.FirstTableReference, optionalSideOf);
                AddSources(inside, cross.SecondTableReference, optionalSideOf);
                break;

            // Not bound, although binding it is easy: what a RIGHT join means for nulls is not modelled yet, so its sources
            // stay unresolved, which makes every column read from them unknown rather than wrongly non-null. Binding it comes
            // with modelling it.
            case QualifiedJoin or UnqualifiedJoin:
                AddUnresolved(adding.Sources, reference, Problem(BindingProblemKind.UnsupportedSource, $"{JoinKeyword(reference)} is not modelled yet.", At(reference)));
                break;
            case JoinParenthesisTableReference parenthesis:
                AddSources(inside, parenthesis.Join, optionalSideOf);
                break;
            case NamedTableReference named:
                // FOR SYSTEM_TIME other than AS OF reads history too, on a table or on a view (observed:
                // For_system_time_other_than_as_of_repeats_a_key_through_a_table_or_a_view).
                // NOLOCK and READUNCOMMITTED read uncommitted rows, a scan of which can repeat a row
                // (docs/decisions/0016-visible-read-uncommitted-drops-key-claims.md).
                adding.Sources.Add(new BoundSource(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value, Named(named, adding.Names), Optional(optionalSideOf, adding.Side), named)
                {
                    History = named.TemporalClause is { TemporalClauseType: not TemporalClauseType.AsOf },
                    Uncommitted = Uncommitted(named),
                });
                break;
            case QueryDerivedTable derived:
            {
                // Its alias is required, and names it in this scope only. Its query reads the enclosing query's scope, or an
                // APPLY's left input's, never the sources beside it.
                var query = Bind(derived.QueryExpression, adding.Outer, adding.Names);
                var symbol = new DerivedTable(derived, query, query.Listed([.. derived.Columns.Select(c => c.Value)]));
                adding.Sources.Add(new BoundSource(derived.Alias?.Value, new RelationTarget(symbol), Optional(optionalSideOf, adding.Side), derived));
                break;
            }

            case SchemaObjectFunctionTableReference call:
            {
                // A table-valued function takes no table hints (a syntax error), so it is never read uncommitted by its own.
                var callee = Callee(call.SchemaObject);
                var symbol = new TableFunctionCall(call, callee, QueryScope.Empty(adding.Outer, adding.Names), FunctionColumns(call, callee));
                adding.Sources.Add(new BoundSource(call.Alias?.Value ?? call.SchemaObject.BaseIdentifier.Value, new RelationTarget(symbol), Optional(optionalSideOf, adding.Side), call));
                break;
            }

            case InlineDerivedTable values:
            {
                // SQL Server requires a name for every column (8155), the same number of values in every row (10709), and
                // distinct names.
                var names = values.Columns.Select(c => c.Value).ToList();
                var listed = names.Count > 0 && values.RowValues.All(r => r.ColumnValues.Count == names.Count)
                    && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
                var symbol = new ValuesTable(values, QueryScope.Empty(adding.Outer, adding.Names), listed ? names : null);
                adding.Sources.Add(new BoundSource(values.Alias?.Value, new RelationTarget(symbol), Optional(optionalSideOf, adding.Side), values));
                break;
            }

            case OpenJsonTableReference openJson:
            {
                List<string> names = openJson.SchemaDeclarationItems.Count == 0
                    ? ["key", "value", "type"]
                    : [.. openJson.SchemaDeclarationItems.Select(i => i.ColumnDefinition.ColumnIdentifier.Value)];
                var symbol = new OpenJsonCall(openJson, QueryScope.Empty(adding.Outer, adding.Names), names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count ? names : null);
                adding.Sources.Add(new BoundSource(openJson.Alias?.Value, new RelationTarget(symbol), Optional(optionalSideOf, adding.Side), openJson));
                break;
            }

            case GlobalFunctionTableReference { Name.Value: var function } series
                when BuiltInFunctions.Named(function, FunctionForm.Rowset) == RowsetFunctions.GenerateSeries:
                adding.Sources.Add(new BoundSource(series.Alias?.Value, new RelationTarget(new SeriesCall(series, QueryScope.Empty(adding.Outer, adding.Names))), Optional(optionalSideOf, adding.Side), series));
                break;
            case VariableTableReference variable:
                // A table variable is the body's own; without a declaration of it in hand it is not modelled.
                adding.Sources.Add(new BoundSource(
                    variable.Alias?.Value ?? variable.Variable.Name,
                    adding.Names.Find(variable.Variable.Name) is { } relation
                        ? new RelationTarget(relation)
                        : new UnresolvedTarget(Problem(BindingProblemKind.UnsupportedSource, $"The table variable {variable.Variable.Name} is not modelled here.", At(reference))),
                    Optional(optionalSideOf, adding.Side),
                    variable));
                break;
            default:
                AddUnresolved(adding.Sources, reference, Problem(BindingProblemKind.UnsupportedSource, $"The table source {FragmentText.OneLine(reference)} is not modelled yet.", At(reference)));
                break;
        }
    }

    /// <summary>A source's optional side, from the location of the outermost one it is inside and how it came about.</summary>
    private static OptionalSide? Optional(SourceLocation? optionalSideOf, Side side) => optionalSideOf is null
        ? null
        : new OptionalSide(optionalSideOf, side switch
        {
            Side.Join => OptionalSideKind.OuterJoin,
            Side.Applied => OptionalSideKind.WholeApplied,
            Side.InsideApplied => OptionalSideKind.InsideApplied,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        });

    /// <summary>Whether a table or view is read with a <c>NOLOCK</c> or <c>READUNCOMMITTED</c> hint.</summary>
    private static bool Uncommitted(NamedTableReference named) =>
        named.TableHints.Any(h => h.HintKind is TableHintKind.NoLock or TableHintKind.ReadUncommitted);

    /// <summary>What a name in FROM reads.</summary>
    private SourceTarget Named(NamedTableReference named, NameEnvironment names)
    {
        var name = named.SchemaObject;
        var at = At(named);
        UnresolvedTarget Unresolved(BindingProblemKind kind, string detail) => new(Problem(kind, detail, at));

        // A CTE hides a table of the same name, but only a one-part name can mean it; a temporary table is only ever a
        // one-part name. Either is the body's own.
        if (IsOnePart(name) && names.Find(name.BaseIdentifier.Value) is { } relation)
            return new RelationTarget(relation);

        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return Unresolved(BindingProblemKind.UnsupportedSource, $"The reference {FragmentText.OneLine(name)} to another database is not modelled yet.");

        ObjectName objectName;
        if (name.SchemaIdentifier is not null)
        {
            objectName = SyntaxReader.Name(name);
        }
        else
        {
            // SQL Server resolves an unqualified name in the caller's default schema, then in dbo, and the
            // source cannot tell which caller. When exactly one object in the model could be read by that
            // name, a query that runs at all has read that one; with more, which is read depends on the caller.
            // Views, synonyms and functions count too: a caller may read one of them instead.
            var candidates = ReadableNamed(name.BaseIdentifier.Value);
            if (candidates.Count > 1)
            {
                return Unresolved(
                    BindingProblemKind.UnresolvedTable,
                    $"{name.BaseIdentifier.Value} is not schema-qualified, and {string.Join(" and ", candidates.Select(c => $"the {c.Kind} {c.Name}"))} have that name: which is read depends on the caller's default schema.");
            }

            objectName = candidates.Count == 1 ? candidates[0].Name : SyntaxReader.Name(name);
        }

        if (Model.FindTableRedefinition(objectName) is { } change)
            return new UnresolvedTarget(Problem(BindingProblemKind.TableRedefined, Redefinitions.Why(change), change.At));

        // A view is read from its definition, by whoever reads the body's values (it may say why it cannot be).
        if (Model.FindReadable(objectName) is View view)
            return new RelationTarget(new ViewRelation(view, ViewColumns(view)));
        if (Model.FindReadable(objectName) is { } other)
            return Unresolved(BindingProblemKind.UnresolvedTable, $"{objectName} is a {KindName(other)}, which is not modelled yet.");

        return Model.FindTable(objectName) is { } table
            ? new TableTarget(table)
            : Unresolved(BindingProblemKind.UnresolvedTable, $"There is no table {objectName} in the project.");
    }

    /// <summary>
    /// A view's columns: its column list (observed: A_view_column_list_names_every_column_and_no_more), or else its query's, with
    /// its stars expanded as SQL Server binds them when it creates the view; null for a view defined more than once, one of
    /// FOR XML or FOR JSON, or one whose columns can't be listed.
    /// </summary>
    private IReadOnlyList<string>? ViewColumns(View view) => bindings.Columns(view, () =>
    {
        if (view.Redefinition is not null || view.Query.QueryExpression is not { } query || ForOf(query) is not null)
            return null;
        var binder = bindings.For(view.Span);
        var names = binder.BindWith(view.Query.WithCtesAndXmlNamespaces, NameEnvironment.View).Names;
        return binder.Bind(query, null, names).Listed(view.Columns);
    });

    /// <summary>
    /// The function a call in FROM names. An unqualified name resolves in the caller's default schema and then in dbo, as a
    /// table's does: the one function of that name when exactly one schema has it.
    /// </summary>
    private Callee Callee(SchemaObjectName name)
    {
        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return new ForeignCallee();
        if (name.SchemaIdentifier is not null)
            return Model.FindFunction(SyntaxReader.Name(name)) is { } function ? new KnownCallee(function) : new MissingCallee();
        var candidates = Model.Functions.Where(f => f.Name.Name.Equals(name.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name.ToString(), StringComparer.Ordinal).ToList();
        return candidates switch
        {
            [] => new MissingCallee(),
            [var function] => new KnownCallee(function),
            _ => new AmbiguousCallee(name.BaseIdentifier.Value, candidates),
        };
    }

    /// <summary>
    /// The columns of what a call in FROM returns: an inline function's query's (its stars not expanded), a multi-statement
    /// or CLR function's declared table's; null for a call with a column list, which SQL Server refuses (317), and for a
    /// function that is not table-valued, is defined more than once, or isn't the project's.
    /// </summary>
    private IReadOnlyList<string>? FunctionColumns(SchemaObjectFunctionTableReference call, Callee callee)
    {
        if (call.Columns.Count > 0 || callee is not KnownCallee { Function: { Redefinition: null } function })
            return null;
        return function switch
        {
            InlineFunction inline => bindings.Columns(inline, () =>
            {
                if (inline.Query.QueryExpression is not { } query || ForOf(query) is not null)
                    return null;
                var binder = bindings.For(inline.Span);
                var names = binder.BindWith(inline.Query.WithCtesAndXmlNamespaces, NameEnvironment.Empty).Names;
                return binder.Bind(query, null, names).Listed([]);
            }),
            TableFunction table => [.. table.Returns.Columns.Select(c => c.Name)],
            ScalarFunction => null,
        };
    }

    /// <summary>
    /// The FOR clause of a query expression: a set operation's is on its last query when it is not a statement's (see
    /// Inference's <c>ForOf</c>, which reads it the same way).
    /// </summary>
    private static ForClause? ForOf(QueryExpression query) => query.ForClause ?? (query is BinaryQueryExpression ? Last(query)?.ForClause : null);

    private static QuerySpecification? Last(QueryExpression query) => query switch
    {
        BinaryQueryExpression binary => Last(binary.SecondQueryExpression),
        QuerySpecification specification => specification,
        _ => null,
    };

    /// <summary>
    /// Adds an unresolvable source for every alias under <paramref name="reference"/>, so that a column
    /// qualified by any of them, or not qualified at all, becomes unbound rather than binding elsewhere.
    /// </summary>
    private static void AddUnresolved(List<BoundSource> sources, TableReference reference, BindingProblem why)
    {
        switch (reference)
        {
            case JoinTableReference join:
                AddUnresolved(sources, join.FirstTableReference, why);
                AddUnresolved(sources, join.SecondTableReference, why);
                break;
            case JoinParenthesisTableReference parenthesis:
                AddUnresolved(sources, parenthesis.Join, why);
                break;
            case NamedTableReference named:
                sources.Add(new BoundSource(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value, new UnresolvedTarget(why), null, named));
                break;
            default:
                sources.Add(new BoundSource((reference as TableReferenceWithAlias)?.Alias?.Value, new UnresolvedTarget(why), null, reference));
                break;
        }
    }

    private static string JoinKeyword(TableReference join) => join switch
    {
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.RightOuter } => "RIGHT OUTER JOIN",
        QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter } => "FULL OUTER JOIN",
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } => "CROSS JOIN",
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossApply } => "CROSS APPLY",
        UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.OuterApply } => "OUTER APPLY",
        _ => "This join",
    };

    // ---- columns ------------------------------------------------------------------------------------------------------

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

    // ---- schema names ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The objects a name written as the target of a write may mean, by the rule <see cref="Bind"/> resolves table names
    /// with: a schema-qualified name means itself; an unqualified one, the one table, view or synonym of that name if exactly
    /// one schema has it, every one of them if more do (which is written depends on the caller's default schema), and
    /// <c>dbo</c>'s when none does. Empty for a name in another database or on a linked server, which the project cannot show.
    /// </summary>
    /// <param name="name">The name as written; a temporary table's or a body's own relation's is not asked about.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(SchemaObjectName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResolveWritten(name.ServerIdentifier is not null || name.DatabaseIdentifier is not null, name.SchemaIdentifier?.Value, name.BaseIdentifier.Value);
    }

    /// <summary>The objects a synonym's name may mean, by the rule <see cref="ResolveWritten(SchemaObjectName)"/> gives.</summary>
    /// <param name="target">The name the synonym stands for.</param>
    public IReadOnlyList<ObjectName> ResolveWritten(SynonymTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ResolveWritten(target.Server is not null || target.Database is not null, target.Schema, target.Name);
    }

    private List<ObjectName> ResolveWritten(bool elsewhere, string? schema, string name)
    {
        if (elsewhere)
            return [];
        if (schema is not null)
            return [new ObjectName(schema, name)];
        var candidates = ReadableNamed(name).Where(c => c.Kind != "function").Select(c => c.Name).Distinct().ToList();
        return candidates.Count > 0 ? candidates : [new ObjectName("dbo", name)];
    }

    /// <summary>
    /// Every object with this name, in any schema, that a FROM clause could read: tables, and the views,
    /// synonyms and functions. In a stable order.
    /// </summary>
    private List<(ObjectName Name, string Kind)> ReadableNamed(string name) =>
    [
        .. Model.Tables.Select(t => (t.Name, Kind: "table"))
            .Concat(Model.Readable.Select(o => (o.Name, Kind: KindName(o))))
            .Where(c => c.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.ToString(), StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal),
    ];

    /// <summary>What a view, synonym or function is, in a word for messages.</summary>
    private static string KindName(SchemaObject readable) => readable switch
    {
        View => "view",
        Synonym => "synonym",
        Function => "function",
        _ => throw new ArgumentException($"{readable.Name} is not an object a FROM clause reads besides a table.", nameof(readable)),
    };

    // ---- EXEC ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds what an <c>EXEC</c> runs: a procedure of the project, or why its results cannot be known from the project. An
    /// unqualified name resolves like a table's (see <see cref="Bind"/>): to the one procedure of that name, if exactly
    /// one schema has it, since SQL Server looks in the caller's default schema and then in <c>dbo</c>.
    /// </summary>
    /// <param name="execute">The <c>EXEC</c>'s specification: of a statement, or of <c>INSERT … EXEC</c>.</param>
    public CallTarget BindCall(ExecuteSpecification execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        var at = At(execute);
        var text = FragmentText.OneLine(execute);
        UnknowableCall Unknowable(UnknowableCallKind kind, string detail, ObjectName? name = null) => new(kind, name, detail, at);

        if (execute.LinkedServer is not null)
            return Unknowable(UnknowableCallKind.MissingProcedure, $"{text} runs on the linked server {execute.LinkedServer.Value}, whose procedures are not in the project.");

        return execute.ExecutableEntity switch
        {
            ExecutableStringList => Unknowable(UnknowableCallKind.DynamicSql, $"{text} runs dynamic SQL, a string built when the procedure runs, so its result sets are not known."),
            ExecutableProcedureReference { ProcedureReference.ProcedureVariable: { } variable } => Unknowable(UnknowableCallKind.ProcedureVariable, $"{text} runs the procedure named in {variable.Name}, which is chosen when it runs."),
            ExecutableProcedureReference { AdHocDataSource: not null } => Unknowable(UnknowableCallKind.MissingProcedure, $"{text} runs a procedure through OPENDATASOURCE, which is not in the project."),
            ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } name } => BindProcedure(name, text, at),
            _ => Unknowable(UnknowableCallKind.MissingProcedure, $"{text} is not a form of EXEC that is modelled."),
        };
    }

    private CallTarget BindProcedure(SchemaObjectName name, string text, SourceLocation at)
    {
        var own = name.BaseIdentifier.Value;

        // sp_executesql is dynamic SQL, under any qualification. Any other sp_ or xp_ name may be one of SQL Server's own
        // procedures, which it runs instead of the database's whatever schema is written (observed), and which ones it has is
        // not known here; so even a procedure of the project with such a name is not followed.
        if (own.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase))
            return new UnknowableCall(UnknowableCallKind.DynamicSql, null, $"{text} runs dynamic SQL, a string built when the procedure runs, so its result sets are not known.", at);
        if (own.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) || own.StartsWith("xp_", StringComparison.OrdinalIgnoreCase))
        {
            var system = new ObjectName(name.SchemaIdentifier?.Value ?? "sys", own);
            var also = Model.FindProcedure(system) is not null ? $" The project's {system} has that name, but SQL Server runs its own procedure of that name instead, if it has one." : "";
            return new UnknowableCall(UnknowableCallKind.SystemProcedure, system, $"{text} runs {system}, which may be a system procedure, whose results are SQL Server's own.{also}", at);
        }

        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return new UnknowableCall(UnknowableCallKind.MissingProcedure, null, $"{text} runs a procedure of another database, which is not in the project.", at);

        if (name.SchemaIdentifier is not null)
        {
            var objectName = SyntaxReader.Name(name);
            return Model.FindProcedure(objectName) is { } procedure
                ? new ProcedureCall(procedure, at)
                : new UnknowableCall(UnknowableCallKind.MissingProcedure, objectName, Missing(objectName, text), at);
        }

        var candidates = Model.Procedures.Where(p => p.Name.Name.Equals(own, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name.ToString(), StringComparer.Ordinal).ToList();
        if (candidates.Count > 1)
        {
            return new UnknowableCall(UnknowableCallKind.AmbiguousProcedure, null,
                $"{text} names {own} without a schema, and {string.Join(" and ", candidates.Select(c => c.Name))} have that name: which runs depends on the caller's default schema.", at);
        }

        var dbo = SyntaxReader.Name(name);
        return candidates.Count == 1 ? new ProcedureCall(candidates[0], at) : new UnknowableCall(UnknowableCallKind.MissingProcedure, dbo, Missing(dbo, text), at);
    }

    private string Missing(ObjectName name, string text) => Model.FindReadable(name) is Synonym
        ? $"{text} runs the synonym {name}, which is not modelled yet."
        : $"{text} runs {name}, which is not a procedure of the project.";

    private static BindingProblem Problem(BindingProblemKind kind, string detail, SourceLocation at) => new(kind, detail, at);

    private SourceLocation At(TSqlFragment fragment) => source.At(fragment);
}
