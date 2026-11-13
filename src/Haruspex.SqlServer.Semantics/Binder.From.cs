using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

// What a FROM clause's table references read: joins and APPLY, names, derived tables and rowsets, each a source of the query's scope.
public sealed partial class Binder
{
    /// <summary>The sources being added to one query's scope, the scope beyond it, and the names in force.</summary>
    private sealed record Adding(List<BoundSource> Sources, QueryScope? Parent, NameEnvironment Names)
    {
        /// <summary>
        /// Inside an APPLY's right side, the scope it reads outer columns from (its left input's sources, then the enclosing
        /// query's); null elsewhere, where a derived table reads only the enclosing query's.
        /// </summary>
        public QueryScope? Lateral { get; init; }

        /// <summary>How the optional side being added to came about: an outer join's, or an OUTER APPLY's right side, whole or inside.</summary>
        public OptionalSideKind Side { get; init; } = OptionalSideKind.OuterJoin;

        /// <summary>The scope a derived table, <c>VALUES</c> list or call here reads beyond itself.</summary>
        public QueryScope? Outer => Lateral ?? Parent;
    }

    /// <param name="adding">The scope being built, and where in its FROM the reference is.</param>
    /// <param name="reference">The table reference.</param>
    /// <param name="optionalSideOf">The outermost optional side it is inside, if any.</param>
    private void AddSources(Adding adding, TableReference reference, SourceLocation? optionalSideOf)
    {
        var optional = Optional(optionalSideOf, adding.Side);
        switch (reference)
        {
            case JoinTableReference or JoinParenthesisTableReference:
                AddJoined(adding, reference, optionalSideOf);
                break;
            case NamedTableReference named:
                // FOR SYSTEM_TIME other than AS OF reads history too, on a table or on a view (observed:
                // For_system_time_other_than_as_of_repeats_a_key_through_a_table_or_a_view).
                // NOLOCK and READUNCOMMITTED read uncommitted rows, a scan of which can repeat a row
                // (docs/decisions/0016-visible-read-uncommitted-drops-key-claims.md).
                adding.Sources.Add(new BoundSource(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value, Named(named, adding.Names), optional, named)
                {
                    History = named.TemporalClause is { TemporalClauseType: not TemporalClauseType.AsOf },
                    Uncommitted = Uncommitted(named),
                });
                break;
            case VariableTableReference variable:
                // A table variable is the body's own; without a declaration of it in hand it is not modelled.
                adding.Sources.Add(new BoundSource(
                    variable.Alias?.Value ?? variable.Variable.Name,
                    adding.Names.Find(variable.Variable.Name) is { } relation
                        ? new RelationTarget(relation)
                        : new UnresolvedTarget(Problem(BindingProblemKind.UnsupportedSource, $"The table variable {variable.Variable.Name} is not modelled here.", At(reference))),
                    optional,
                    variable));
                break;
            default:
                if (Relation(adding, reference) is { } bound)
                    adding.Sources.Add(new BoundSource(bound.Alias, new RelationTarget(bound.Relation), optional, reference));
                else
                    AddUnresolved(adding.Sources, reference, Problem(BindingProblemKind.UnsupportedSource, $"The table source {FragmentText.OneLine(reference)} is not modelled yet.", At(reference)));
                break;
        }
    }

    /// <summary>
    /// The sources of a join, an APPLY or a parenthesised join, each on the optional side it is on: a LEFT JOIN's right side,
    /// either side of a FULL JOIN, an OUTER APPLY's right side; inside an outer join's optional side the enclosing one is kept,
    /// as for nested outer joins.
    /// </summary>
    private void AddJoined(Adding adding, TableReference reference, SourceLocation? optionalSideOf)
    {
        // A source joined inside an OUTER APPLY's right side is on that side, but is not the whole of it.
        var inside = adding with { Side = adding.Side == OptionalSideKind.WholeApplied ? OptionalSideKind.InsideApplied : adding.Side };
        var asJoin = inside with { Side = OptionalSideKind.OuterJoin };

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
                    AddSources(adding with { Lateral = left, Side = OptionalSideKind.WholeApplied }, apply.SecondTableReference, At(apply.SecondTableReference));
                else
                    AddSources(inside with { Lateral = left }, apply.SecondTableReference, optionalSideOf);
                break;
            }

            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.FullOuter } join:
                // Each side has no row where nothing on the other matches, so both are optional, each at its own location
                // (observed: A_full_outer_join_null_extends_both_sides_where_nothing_matches).
                AddSources(optionalSideOf is null ? asJoin : inside, join.FirstTableReference, optionalSideOf ?? At(join.FirstTableReference));
                AddSources(optionalSideOf is null ? asJoin : inside, join.SecondTableReference, optionalSideOf ?? At(join.SecondTableReference));
                break;
            case UnqualifiedJoin { UnqualifiedJoinType: UnqualifiedJoinType.CrossJoin } cross:
                // Every row of one with every row of the other: both sides read as they are.
                AddSources(inside, cross.FirstTableReference, optionalSideOf);
                AddSources(inside, cross.SecondTableReference, optionalSideOf);
                break;
            case JoinParenthesisTableReference parenthesis:
                AddSources(inside, parenthesis.Join, optionalSideOf);
                break;

            // Not bound, although binding it is easy: what a RIGHT join means for nulls is not modelled yet, so its sources
            // stay unresolved, which makes every column read from them unknown rather than wrongly non-null. Binding it comes
            // with modelling it.
            default:
                AddUnresolved(adding.Sources, reference, Problem(BindingProblemKind.UnsupportedSource, $"{JoinKeyword(reference)} is not modelled yet.", At(reference)));
                break;
        }
    }

    /// <summary>
    /// The relation a derived table or a rowset in FROM is, with the alias its columns are qualified by: a derived table, a
    /// table-valued function's call, a VALUES list, OPENJSON or GENERATE_SERIES. Each reads the enclosing query's scope, or an
    /// APPLY's left input's, never the sources beside it. Null for any other reference.
    /// </summary>
    private (string? Alias, RelationSymbol Relation)? Relation(Adding adding, TableReference reference)
    {
        switch (reference)
        {
            case QueryDerivedTable derived:
            {
                // Its alias is required, and names it in this scope only.
                var query = Bind(derived.QueryExpression, adding.Outer, adding.Names);
                return (derived.Alias?.Value, new DerivedTable(derived, query, query.Listed([.. derived.Columns.Select(c => c.Value)])));
            }

            case SchemaObjectFunctionTableReference call:
            {
                // A table-valued function takes no table hints (a syntax error), so it is never read uncommitted by its own.
                var callee = Callee(call.SchemaObject);
                return (call.Alias?.Value ?? call.SchemaObject.BaseIdentifier.Value, new TableFunctionCall(call, callee, QueryScope.Empty(adding.Outer, adding.Names), FunctionColumns(call, callee)));
            }

            case InlineDerivedTable values:
            {
                // SQL Server requires a name for every column (8155), the same number of values in every row (10709), and
                // distinct names.
                var names = values.Columns.Select(c => c.Value).ToList();
                var listed = names.Count > 0 && values.RowValues.All(r => r.ColumnValues.Count == names.Count)
                    && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
                return (values.Alias?.Value, new ValuesTable(values, QueryScope.Empty(adding.Outer, adding.Names), listed ? names : null));
            }

            case OpenJsonTableReference openJson:
            {
                List<string> names = openJson.SchemaDeclarationItems.Count == 0
                    ? ["key", "value", "type"]
                    : [.. openJson.SchemaDeclarationItems.Select(i => i.ColumnDefinition.ColumnIdentifier.Value)];
                return (openJson.Alias?.Value, new OpenJsonCall(openJson, QueryScope.Empty(adding.Outer, adding.Names), names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count ? names : null));
            }

            case GlobalFunctionTableReference { Name.Value: var function } series
                when BuiltInFunctions.Named(function, FunctionForm.Rowset) == RowsetFunctions.GenerateSeries:
                return (series.Alias?.Value, new SeriesCall(series, QueryScope.Empty(adding.Outer, adding.Names)));
            default:
                return null;
        }
    }

    /// <summary>A source's optional side, from the location of the outermost one it is inside and how it came about.</summary>
    private static OptionalSide? Optional(SourceLocation? optionalSideOf, OptionalSideKind side) =>
        optionalSideOf is null ? null : new OptionalSide(optionalSideOf, side);

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
}
