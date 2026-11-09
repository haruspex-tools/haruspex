using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// Binds the references in one body to model objects: table sources in FROM clauses, with their aliases and
/// scopes, and column references to the columns they read. Every reference it cannot bind for certain becomes a
/// <see cref="BindingProblem"/>, never a guess.
/// </summary>
/// <param name="model">The schema the body runs against.</param>
/// <param name="file">The file the body is in, for locations.</param>
public sealed class Binder(DatabaseModel model, string file)
{
    /// <summary>Binds a query's FROM clause into a new scope inside <paramref name="parent"/>.</summary>
    public BindingScope BindFrom(QuerySpecification query, BindingScope? parent)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = new BindingScope(parent);
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            AddSources(reference, scope, optionalSideOf: null);
        return scope;
    }

    /// <summary>Binds a column reference, looking in <paramref name="scope"/> first and then outwards.</summary>
    public ColumnBinding BindColumn(ColumnReferenceExpression column, BindingScope scope)
    {
        ArgumentNullException.ThrowIfNull(column);
        var at = At(column);
        if (column.ColumnType != ColumnType.Regular || column.MultiPartIdentifier is null)
            return Unbound(BindingProblemKind.UnsupportedReference, $"The column reference {FragmentText.OneLine(column)} is not modelled yet.", at);

        var parts = column.MultiPartIdentifier.Identifiers;
        if (parts.Count > 2)
            return Unbound(BindingProblemKind.UnsupportedReference, $"The multi-part column reference {FragmentText.OneLine(column)} is not modelled yet.", at);

        var name = parts[^1].Value;

        // Innermost scope first: a correlated subquery sees its own sources before the outer query's.
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (parts.Count == 2)
            {
                var qualifier = parts[0].Value;
                if (s.Sources.FirstOrDefault(x => qualifier.Equals(x.Alias, StringComparison.OrdinalIgnoreCase)) is { } source)
                    return FromSource(source, name, at);
                continue;
            }

            var matches = s.Sources.Where(x => x.Table?.Column(name) is not null).ToList();
            if (matches.Count > 1)
                return Unbound(BindingProblemKind.UnresolvedColumn, $"The column {name} is ambiguous: more than one table in scope has it.", at);
            if (matches.Count == 1)
                return FromSource(matches[0], name, at);

            // A source that cannot be bound may be the one that has this column.
            if (s.Sources.FirstOrDefault(x => x.Table is null) is { } unresolved)
                return FromSource(unresolved, name, at);
        }

        return Unbound(BindingProblemKind.UnresolvedColumn, $"No table in scope has the column {FragmentText.OneLine(column)}.", at);
    }

    private static ColumnBinding FromSource(BoundSource source, string name, SourceLocation at)
    {
        // The source's own problem, moved to the column, so that each reference is reported where it is.
        if (source.Table is null)
            return new UnboundColumn(source.Unresolved! with { At = at });

        return source.Table.Column(name) is { } column
            ? new BoundColumn(source, source.Table, column)
            : Unbound(BindingProblemKind.UnresolvedColumn, $"The table {source.Table.Name} has no column {name}.", at);
    }

    private void AddSources(TableReference reference, BindingScope scope, SourceLocation? optionalSideOf)
    {
        switch (reference)
        {
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join:
                AddSources(join.FirstTableReference, scope, optionalSideOf);
                AddSources(join.SecondTableReference, scope, optionalSideOf);
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join:
                // The right side has no row when nothing matches. Recorded on each of its sources, so a reader
                // can tell that any column read from them may have no value, whatever its declaration says.
                AddSources(join.FirstTableReference, scope, optionalSideOf);
                AddSources(join.SecondTableReference, scope, optionalSideOf ?? At(join.SecondTableReference));
                break;
            // Not bound, although binding them is easy: what a RIGHT or FULL join or an APPLY means for nulls is
            // not modelled yet, so their sources stay unresolved, which makes every column read from them unknown
            // rather than wrongly non-null. Binding them comes with modelling them.
            case QualifiedJoin or UnqualifiedJoin:
                AddUnresolved(reference, scope, Problem(BindingProblemKind.UnsupportedSource, $"{JoinKeyword(reference)} is not modelled yet.", At(reference)));
                break;
            case JoinParenthesisTableReference parenthesis:
                AddSources(parenthesis.Join, scope, optionalSideOf);
                break;
            case NamedTableReference named:
                scope.Add(Named(named, optionalSideOf));
                break;
            default:
                AddUnresolved(reference, scope, Problem(BindingProblemKind.UnsupportedSource, $"The table source {FragmentText.OneLine(reference)} is not modelled yet.", At(reference)));
                break;
        }
    }

    private BoundSource Named(NamedTableReference named, SourceLocation? optionalSideOf)
    {
        var name = named.SchemaObject;
        var alias = named.Alias?.Value ?? name.BaseIdentifier.Value;
        var at = At(named);
        BoundSource Unresolved(BindingProblemKind kind, string detail) => new(alias, null, Problem(kind, detail, at), optionalSideOf);

        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return Unresolved(BindingProblemKind.UnsupportedSource, $"The reference {FragmentText.OneLine(name)} to another database is not modelled yet.");
        if (name.BaseIdentifier.Value.StartsWith('#'))
            return Unresolved(BindingProblemKind.UnsupportedSource, $"The temporary table {name.BaseIdentifier.Value} is not modelled yet.");

        ObjectName objectName;
        if (name.SchemaIdentifier is not null)
        {
            objectName = ObjectName.From(name);
        }
        else
        {
            // SQL Server resolves an unqualified name in the caller's default schema, then in dbo, and the
            // source cannot tell which caller. When exactly one object in the model could be read by that
            // name, a query that runs at all has read that one; with more, which is read depends on the caller.
            // Views, synonyms and functions count too: they are not modelled, but a caller may read one of them.
            var candidates = ReadableNamed(name.BaseIdentifier.Value);
            if (candidates.Count > 1)
            {
                return Unresolved(
                    BindingProblemKind.UnresolvedTable,
                    $"{name.BaseIdentifier.Value} is not schema-qualified, and {string.Join(" and ", candidates.Select(c => $"the {c.Kind} {c.Name}"))} have that name: which is read depends on the caller's default schema.");
            }

            objectName = candidates.Count == 1 ? candidates[0].Name : ObjectName.From(name);
        }

        if (model.FindTableRedefinition(objectName) is { } change)
            return new BoundSource(alias, null, Problem(BindingProblemKind.TableRedefined, change.Message, change.At), optionalSideOf);

        if (model.FindNamedObject(objectName) is { } other)
            return Unresolved(BindingProblemKind.UnresolvedTable, $"{objectName} is a {KindName(other.Kind)}, which is not modelled yet.");

        var table = model.FindTable(objectName);
        return table is not null
            ? new BoundSource(alias, table, null, optionalSideOf)
            : Unresolved(BindingProblemKind.UnresolvedTable, $"There is no table {objectName} in the project.");
    }

    /// <summary>
    /// Every object with this name, in any schema, that a FROM clause could read: tables, and the views,
    /// synonyms and functions that are not modelled. In a stable order.
    /// </summary>
    private List<(ObjectName Name, string Kind)> ReadableNamed(string name) =>
    [
        .. model.Tables.Select(t => (Name: t.Name, Kind: "table"))
            .Concat(model.NamedObjects.Select(o => (Name: o.Name, Kind: KindName(o.Kind))))
            .Where(c => c.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.ToString(), StringComparer.Ordinal)
            .ThenBy(c => c.Kind, StringComparer.Ordinal),
    ];

    private static string KindName(NamedObjectKind kind) => kind switch
    {
        NamedObjectKind.View => "view",
        NamedObjectKind.Synonym => "synonym",
        NamedObjectKind.Function => "function",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
    };

    /// <summary>
    /// Adds an unresolvable source for every alias under <paramref name="reference"/>, so that a column
    /// qualified by any of them, or not qualified at all, becomes unbound rather than binding elsewhere.
    /// </summary>
    private static void AddUnresolved(TableReference reference, BindingScope scope, BindingProblem why)
    {
        switch (reference)
        {
            case JoinTableReference join:
                AddUnresolved(join.FirstTableReference, scope, why);
                AddUnresolved(join.SecondTableReference, scope, why);
                break;
            case JoinParenthesisTableReference parenthesis:
                AddUnresolved(parenthesis.Join, scope, why);
                break;
            case NamedTableReference named:
                scope.Add(new BoundSource(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value, null, why, null));
                break;
            default:
                scope.Add(new BoundSource((reference as TableReferenceWithAlias)?.Alias?.Value, null, why, null));
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

    private static BindingProblem Problem(BindingProblemKind kind, string detail, SourceLocation at) => new(kind, detail, at);

    private static UnboundColumn Unbound(BindingProblemKind kind, string detail, SourceLocation at) => new(Problem(kind, detail, at));

    private SourceLocation At(TSqlFragment fragment) => new(file, fragment.StartLine, fragment.StartColumn);
}
