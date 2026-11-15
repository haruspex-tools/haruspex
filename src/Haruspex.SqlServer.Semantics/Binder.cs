using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Binds the names in one body to what they mean: each query's FROM into a scope of sources (tables of the schema, the
/// body's own relations, views and calls, each a <see cref="RelationSymbol"/>), with derived tables, <c>APPLY</c> sides and
/// CTEs bound recursively into a tree (<see cref="BoundQuery"/>); each column reference to the column it reads
/// (<see cref="Resolve(ColumnReferenceExpression, QueryScope)"/>); each <c>EXEC</c> to the procedure it runs. Every reference it cannot bind for certain becomes a
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
public sealed partial class Binder
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

    /// <summary>What a body declares, read once for each body (<see cref="Bindings.Declarations"/>).</summary>
    /// <param name="parameters">The procedure's or function's parameters.</param>
    /// <param name="body">Its statements; null for a body of one expression or query.</param>
    /// <param name="returnTable">A multi-statement function's return table, or null.</param>
    public BodyDeclarations Declarations(IReadOnlyList<Parameter> parameters, StatementList? body = null, DeclaredTable? returnTable = null) =>
        bindings.Declarations(parameters, body, returnTable, source);

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

            return new BoundWith(inForce, [.. ctes]);
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
    /// A query's columns' names (<see cref="ColumnName"/>), a star's being its columns'; null when a star can't be expanded.
    /// </summary>
    private EquatableArray<string?>? Names(QuerySpecification query, QueryScope scope)
    {
        var names = new List<string?>();
        foreach (var element in query.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    names.Add(ColumnName(scalar));
                    break;
                case SelectStarExpression star when Star(star, scope) is ExpandedStar { Columns: var columns }:
                    names.AddRange(columns.Select(c => c.Name));
                    break;
                case SelectStarExpression:
                    return null;
            }
        }

        return [.. names];
    }

    // ---- SELECT * -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// What <c>*</c> or <c>alias.*</c> stands for: each source's columns in order, a table's in the order declared (observed:
    /// A_star_gives_a_tables_columns_in_column_id_order) and a relation's as it names them. A table's star leaves out a hidden
    /// period column (observed: A_star_leaves_out_hidden_period_columns), and a table with a column set gives the set in place of
    /// its sparse columns (observed: A_column_set_stands_for_its_sparse_columns_in_star). It is bound where the query is, against
    /// the tables as the project defines them (decision 0047), and against a temporary table's columns as the walk knows them
    /// at the statement.
    /// </summary>
    /// <param name="star">The star.</param>
    /// <param name="scope">The scope of the query whose select list it is in.</param>
    public StarExpansion Star(SelectStarExpression star, QueryScope scope)
    {
        ArgumentNullException.ThrowIfNull(star);
        ArgumentNullException.ThrowIfNull(scope);
        IEnumerable<BoundSource> sources = scope.Sources;

        // Observed: A_star_without_from_is_refused (263, when the module is created), so it never returns a row.
        if (scope.Sources.Count == 0)
            return Unexpanded(BindingProblemKind.UnsupportedStar, "A star needs a FROM, and SQL Server refuses one without it.", star);
        if (star.Qualifier is { } qualifier)
        {
            var text = string.Join('.', qualifier.Identifiers.Select(i => i.Value));
            if (qualifier.Identifiers.Count != 1)
                return Unexpanded(BindingProblemKind.UnsupportedStar, $"{text}.* names its source by more than one part, which is not modelled yet.", star);
            var named = scope.Sources.Where(s => qualifier.Identifiers[0].Value.Equals(s.Alias, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count != 1)
                return Unexpanded(BindingProblemKind.UnresolvedColumn, $"{text}.* names no one source of this query.", star);
            sources = named;
        }

        var columns = new List<StarColumn>();
        foreach (var source in sources)
        {
            switch (source.Target)
            {
                case TableTarget { Table: { IsGraph: true } table }:
                    // Observed: A_graph_tables_star_adds_pseudo_columns_named_by_the_server ($node_id_ and a suffix SQL Server
                    // generates when it creates the table), so their names are not in the project.
                    return Unexpanded(BindingProblemKind.UnsupportedStar, $"{table.Name} is a graph table, whose * adds pseudo-columns named by the server when it creates the table.", star);
                case TableTarget { Table: var table }:
                    columns.AddRange(Starred(table).Select(c => new StarColumn(c.Column.Name, new TableColumn(source, table, c.Column))));
                    break;
                case RelationTarget { Relation: PseudoTable { Written: { } written } pseudo }:
                    columns.AddRange(Starred(written).Select(c => new StarColumn(c.Column.Name, new RelationColumn(source, pseudo, c.Ordinal))));
                    break;
                case RelationTarget { Relation: TableVariable { Declaration: var declared } variable }:
                    columns.AddRange(Starred(declared).Select(c => new StarColumn(c.Column.Name, new RelationColumn(source, variable, c.Ordinal))));
                    break;
                case RelationTarget { Relation: { Columns: { } names } relation }:
                    columns.AddRange(names.Select((n, i) => new StarColumn(n, new RelationColumn(source, relation, i))));
                    break;
                case RelationTarget:
                    return new UnlistedStar(source);
                case UnresolvedTarget { Problem: var problem }:
                    return new UnexpandedStar(problem);
            }
        }

        return new ExpandedStar([.. columns]);
    }

    /// <summary>
    /// The columns a table's or a table variable's star gives, in order, each with its place among the declared columns: all but
    /// a hidden one, and, when the table has a column set, all but its sparse columns, which the set stands for.
    /// </summary>
    private static IEnumerable<(Column Column, int Ordinal)> Starred(ITable table)
    {
        var columnSet = table.Columns.Any(c => c.IsColumnSet);
        return table.Columns.Select((c, i) => (c, i)).Where(c => !c.c.IsHidden && !(columnSet && c.c.IsSparse));
    }

    private UnexpandedStar Unexpanded(BindingProblemKind kind, string detail, SelectStarExpression star) => new(Problem(kind, detail, At(star)));

    private static BindingProblem Problem(BindingProblemKind kind, string detail, SourceLocation at) => new(kind, detail, at);

    private SourceLocation At(TSqlFragment fragment) => source.At(fragment);
}
