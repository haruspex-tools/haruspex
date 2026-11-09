using System.Text;
using Haruspex.Ir;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>What inference found in one procedure.</summary>
/// <param name="Parameters">Each declared parameter and its type.</param>
/// <param name="Results">The result sets in order, or null when a statement makes them impossible to list.</param>
/// <param name="ResultsUnknown">Why the results cannot be listed, when <paramref name="Results"/> is null.</param>
internal sealed record ProcedureAnalysis(
    IReadOnlyList<ParameterShape> Parameters,
    IReadOnlyList<ResultShape>? Results,
    UnknownShape? ResultsUnknown);

internal sealed record ParameterShape(string Name, Shape Type, bool HasDefault, bool IsOutput);

/// <param name="AbsentBecause">Why the result can arrive with no value at all (see <see cref="IrResult"/>).</param>
internal sealed record ResultShape(IrEncoding Encoding, Shape Shape, IReadOnlyList<IrNullReason> AbsentBecause);

/// <summary>
/// Infers the result sets of one procedure. Every construct it does not model becomes an
/// <see cref="UnknownShape"/> with a reason, so what it does claim can be trusted.
/// </summary>
internal sealed class ProcedureAnalyzer(Catalog catalog, string file)
{
    public ProcedureAnalysis Analyse(ProcedureStatementBody procedure)
    {
        var parameters = procedure.Parameters
            .Select(p => new ParameterShape(
                p.VariableName.Value,
                SqlTypes.ToShape(p.DataType, At(p)),
                HasDefault: p.Value is not null,
                IsOutput: p.Modifier == ParameterModifier.Output))
            .ToList();

        // A CLR procedure's body is .NET code, so its results cannot be known from the project.
        if (procedure.MethodSpecifier is not null || procedure.StatementList is null)
        {
            return new ProcedureAnalysis(parameters, null, new UnknownShape(
                IrUnknownReasons.ExternalCode,
                "This is a CLR procedure: its results come from .NET code, which is not in the project.",
                At(procedure)));
        }

        var results = new List<ResultShape>();
        var blocker = Walk(procedure.StatementList.Statements, results);
        return blocker is null
            ? new ProcedureAnalysis(parameters, results, null)
            : new ProcedureAnalysis(parameters, null, blocker);
    }

    // ---- statements ---------------------------------------------------------------------------------

    /// <summary>
    /// Collects the result sets of a statement list. Returns the first statement that makes the list of
    /// results unknowable, because it could return results of its own or decide which others run.
    /// </summary>
    private UnknownShape? Walk(IEnumerable<TSqlStatement> statements, List<ResultShape> results)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case PredicateSetStatement set when IsHarmless(set):
                    break;

                // These never return a result set. Variables are not tracked yet, so a later reference to one
                // is an unknown expression rather than a wrong type.
                case DeclareVariableStatement:
                case DeclareTableVariableStatement:
                case SetVariableStatement:
                    break;
                case BeginEndBlockStatement block:
                    if (Walk(block.StatementList.Statements, results) is { } blocker)
                        return blocker;
                    break;
                case SelectStatement select:
                    if (Select(select) is { } result)
                        results.Add(result);
                    break;
                default:
                    var keyword = statement.ScriptTokenStream[statement.FirstTokenIndex].Text.ToUpperInvariant();
                    return new UnknownShape(
                        IrUnknownReasons.UnsupportedStatement,
                        $"{keyword} is not modelled yet, so the procedure's result sets cannot be listed.",
                        At(statement));
            }
        }

        return null;
    }

    /// <summary>
    /// Session options known not to change which result sets run or their shape. Anything else is not modelled:
    /// NOEXEC, FMTONLY and PARSEONLY stop the rows from being returned at all, and turning off ANSI_WARNINGS
    /// or CONCAT_NULL_YIELDS_NULL changes where expressions produce null.
    /// </summary>
    private static readonly SetOptions AnyValue = SetOptions.NoCount | SetOptions.XactAbort;

    private static readonly SetOptions OnlyOn =
        SetOptions.AnsiNulls | SetOptions.AnsiPadding | SetOptions.AnsiWarnings | SetOptions.QuotedIdentifier | SetOptions.ArithAbort;

    private static bool IsHarmless(PredicateSetStatement set) =>
        (set.Options & ~(set.IsOn ? AnyValue | OnlyOn : AnyValue)) == SetOptions.None;

    /// <summary>The result set a SELECT returns, or null if it returns none.</summary>
    private ResultShape? Select(SelectStatement select)
    {
        // SELECT ... INTO writes a table and SELECT @v = ... assigns variables; neither returns a result set.
        if (select.Into is not null)
            return null;
        if (select.QueryExpression is QuerySpecification { SelectElements: var elements } && elements.All(e => e is SelectSetVariable))
            return null;

        var at = At(select);
        var encoding = select.QueryExpression.ForClause is JsonForClause ? IrEncoding.Json : IrEncoding.Rowset;

        // A top-level FOR JSON query that matches no rows returns no row at all, rather than [] or {}. A
        // rowset that matches no rows is still a result set, with no rows.
        IReadOnlyList<IrNullReason> absent = encoding == IrEncoding.Json ? [new IrNullReason(IrNullRules.NoRows, at.ToString())] : [];

        if (select.WithCtesAndXmlNamespaces is not null)
            return new ResultShape(encoding, Unsupported(IrUnknownReasons.UnsupportedQuery, "A common table expression (WITH) is not modelled yet.", at), absent);
        if (select.QueryExpression is not QuerySpecification query)
            return new ResultShape(encoding, Unsupported(IrUnknownReasons.UnsupportedQuery, "A UNION, INTERSECT, EXCEPT or parenthesised query is not modelled yet.", at), absent);

        var row = Query(query, parent: null);
        return query.ForClause switch
        {
            JsonForClause json => new ResultShape(IrEncoding.Json, ForJson(row, json, at), absent),
            null => new ResultShape(IrEncoding.Rowset, Rowset(row), absent),
            _ => new ResultShape(IrEncoding.Rowset, Unsupported(IrUnknownReasons.UnsupportedQuery, $"{Text(query.ForClause)} is not modelled yet.", at), []),
        };
    }

    // ---- queries ------------------------------------------------------------------------------------

    private sealed record Row(IReadOnlyList<RowColumn> Columns, SourceLocation? Star);

    private Row Query(QuerySpecification query, Scope? parent)
    {
        var scope = new Scope(parent);
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            AddSources(reference, scope, outerJoin: null);

        // WHERE, GROUP BY and HAVING are not read. They can only remove rows or make values non-null, so
        // ignoring them can leave a field nullable that is not, but never the reverse.
        var columns = new List<RowColumn>();
        SourceLocation? star = null;
        foreach (var element in query.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    var name = scalar.ColumnName?.Value
                        ?? (scalar.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers[^1].Value;
                    columns.Add(new RowColumn(name, Expression(scalar.Expression, scope), At(scalar)));
                    break;
                case SelectStarExpression:
                    star ??= At(element);
                    break;
            }
        }

        return new Row(columns, star);
    }

    private static Shape Rowset(Row row)
    {
        if (row.Star is { } star)
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star);
        if (row.Columns.FirstOrDefault(c => string.IsNullOrEmpty(c.Name)) is { } unnamed)
            return Unsupported(IrUnknownReasons.UnnamedColumn, $"The column {Text(unnamed.At)} has no name.", unnamed.At);

        // Drivers key a row by column name, and the mssql driver turns a repeated name into an array, so a
        // repeated name changes the row's shape.
        var duplicate = row.Columns.GroupBy(c => c.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Unsupported(IrUnknownReasons.DuplicateColumn, $"The column name '{duplicate.Key}' is used more than once.", duplicate.Skip(1).First().At);

        var fields = row.Columns.Select(c => new FieldShape(
            c.Name!,
            // A rowset column delivers JSON as text: the driver sees an nvarchar.
            c.Value.IsLiveJson ? new JsonTextShape() : c.Value.Shape,
            IrPresence.Always,
            c.Value.NullableBecause));
        return new ArrayShape(new ObjectShape([.. fields]));
    }

    // ---- FOR JSON -----------------------------------------------------------------------------------

    private sealed class JsonNode
    {
        public List<(string Key, JsonNode? Child, RowColumn? Leaf, SourceLocation At)> Entries { get; } = [];
    }

    /// <summary>
    /// The shape <c>FOR JSON PATH</c> builds from a row: one object per row, with a dotted alias such as
    /// <c>customer.name</c> nesting a key inside an object.
    /// </summary>
    private Shape ForJson(Row row, JsonForClause clause, SourceLocation at)
    {
        var options = clause.Options.Select(o => o.OptionKind).ToHashSet();
        if (options.Contains(JsonForClauseOptions.Auto))
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "FOR JSON AUTO is not modelled yet.", At(clause));
        if (options.Contains(JsonForClauseOptions.Root))
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "FOR JSON ... ROOT is not modelled yet.", At(clause));
        if (row.Star is { } star)
            return Unsupported(IrUnknownReasons.UnsupportedQuery, "SELECT * is not expanded yet.", star);

        var root = new JsonNode();
        foreach (var column in row.Columns)
        {
            // SQL Server rejects an unnamed column in FOR JSON, so the procedure fails rather than returning
            // anything; it is reported rather than modelled.
            if (string.IsNullOrEmpty(column.Name))
                return Unsupported(IrUnknownReasons.UnnamedColumn, $"The column {Text(column.At)} has no name, which FOR JSON rejects.", column.At);

            var path = column.Name.Split('.');
            if (path.Any(p => p.Length == 0))
                return Unsupported(IrUnknownReasons.UnsupportedQuery, $"The alias '{column.Name}' is not a valid FOR JSON path.", column.At);

            var node = root;
            foreach (var key in path[..^1])
            {
                // Keys of one nested object must come from adjacent columns. Anything else (a key reused after
                // another column, a key that is both a value and an object, or the same key spelt in another
                // case) either fails in SQL Server or writes duplicate keys, so it is not modelled.
                if (node.Entries.Count > 0 && node.Entries[^1] is { Child: { } child } last && last.Key.Equals(key, StringComparison.Ordinal))
                {
                    node = child;
                    continue;
                }

                if (node.Entries.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    return KeyConflict(column);
                var nested = new JsonNode();
                node.Entries.Add((key, nested, null, column.At));
                node = nested;
            }

            if (node.Entries.Any(e => e.Key.Equals(path[^1], StringComparison.OrdinalIgnoreCase)))
                return KeyConflict(column);
            node.Entries.Add((path[^1], null, column, column.At));
        }

        var includeNulls = options.Contains(JsonForClauseOptions.IncludeNullValues);
        var shape = JsonObject(root, includeNulls);

        // WITHOUT_ARRAY_WRAPPER is claimed as one object although nothing here proves the query returns at most
        // one row (WHERE is not read; recognising single-row queries is a known gap). The claim is accepted
        // because it cannot be silently wrong: several rows produce "{...},{...}", which is not valid JSON, so
        // the caller's parse fails loudly before any schema sees it. No rows are covered by AbsentBecause.
        return options.Contains(JsonForClauseOptions.WithoutArrayWrapper) ? shape : new ArrayShape(shape);
    }

    private static UnknownShape KeyConflict(RowColumn column) =>
        new(IrUnknownReasons.DuplicateColumn, $"The alias '{column.Name}' clashes with another key in the same JSON object.", column.At);

    private static ObjectShape JsonObject(JsonNode node, bool includeNulls)
    {
        var fields = new List<FieldShape>();
        foreach (var (key, child, leaf, at) in node.Entries)
        {
            if (leaf is not null)
            {
                var value = leaf.Value;

                // FOR JSON leaves out a key whose value is null, unless INCLUDE_NULL_VALUES keeps it as null.
                // Live JSON keeps its shape; JSON text (JsonTextShape) is escaped into a string.
                var presence = value.NullableBecause.Count > 0 && !includeNulls ? IrPresence.OmittedWhenNull : IrPresence.Always;
                fields.Add(new FieldShape(key, value.Shape, presence, value.NullableBecause));
                continue;
            }

            var nested = JsonObject(child!, includeNulls);

            // A nested object whose every value is null is left out of the output. With INCLUDE_NULL_VALUES SQL
            // Server is believed to keep it, with its keys as null; until the differential tests confirm that,
            // the object is claimed only as possibly absent, which holds either way.
            var allNull = nested.Fields.All(f => f.NullableBecause.Count > 0);
            fields.Add(allNull
                ? new FieldShape(key, nested, IrPresence.OmittedWhenNull, [new IrNullReason(IrNullRules.AllFieldsNull, at.ToString())])
                : new FieldShape(key, nested, IrPresence.Always, []));
        }

        return new ObjectShape(fields);
    }

    // ---- sources and scopes -------------------------------------------------------------------------

    private sealed class Scope(Scope? parent)
    {
        public Scope? Parent { get; } = parent;

        public List<Source> Sources { get; } = [];
    }

    /// <param name="Alias">The name columns are qualified by: the alias, or the table's name without one.</param>
    /// <param name="Table">The table, or null when the source cannot be resolved.</param>
    /// <param name="Unresolved">Why the source cannot be resolved.</param>
    /// <param name="OuterJoin">Set when the source is on the optional side of an outer join.</param>
    private sealed record Source(string? Alias, TableModel? Table, UnknownShape? Unresolved, IrNullReason? OuterJoin);

    private void AddSources(TableReference reference, Scope scope, IrNullReason? outerJoin)
    {
        switch (reference)
        {
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.Inner } join:
                AddSources(join.FirstTableReference, scope, outerJoin);
                AddSources(join.SecondTableReference, scope, outerJoin);
                break;
            case QualifiedJoin { QualifiedJoinType: QualifiedJoinType.LeftOuter } join:
                // The right side has no row when nothing matches, so every column read from it can be null,
                // whatever its declaration says.
                AddSources(join.FirstTableReference, scope, outerJoin);
                AddSources(join.SecondTableReference, scope, outerJoin ?? new IrNullReason(IrNullRules.OuterJoin, At(join.SecondTableReference).ToString()));
                break;
            case QualifiedJoin or UnqualifiedJoin:
                AddUnresolved(reference, scope, Unsupported(IrUnknownReasons.UnsupportedSource, $"{JoinKeyword(reference)} is not modelled yet.", At(reference)));
                break;
            case JoinParenthesisTableReference parenthesis:
                AddSources(parenthesis.Join, scope, outerJoin);
                break;
            case NamedTableReference named:
                scope.Sources.Add(Named(named, outerJoin));
                break;
            default:
                AddUnresolved(reference, scope, Unsupported(IrUnknownReasons.UnsupportedSource, $"The table source {Text(reference)} is not modelled yet.", At(reference)));
                break;
        }
    }

    private Source Named(NamedTableReference named, IrNullReason? outerJoin)
    {
        var name = named.SchemaObject;
        var alias = named.Alias?.Value ?? name.BaseIdentifier.Value;
        var at = At(named);
        Source Unresolved(string reason, string detail) => new(alias, null, Unsupported(reason, detail, at), outerJoin);

        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return Unresolved(IrUnknownReasons.UnsupportedSource, $"The reference {Text(name)} to another database is not modelled yet.");
        if (name.BaseIdentifier.Value.StartsWith('#'))
            return Unresolved(IrUnknownReasons.UnsupportedSource, $"The temporary table {name.BaseIdentifier.Value} is not modelled yet.");

        ObjectName objectName;
        if (name.SchemaIdentifier is not null)
        {
            objectName = ObjectName.From(name);
        }
        else
        {
            // SQL Server resolves an unqualified name in the caller's default schema, then in dbo, and the
            // source cannot tell which caller. When exactly one object in the project could be read by that
            // name, a query that runs at all has read that one; with more, which is read depends on the caller.
            // Views, synonyms and functions count too: they are not modelled, but a caller may read one of them.
            var candidates = catalog.ReadableNamed(name.BaseIdentifier.Value);
            if (candidates.Count > 1)
            {
                return Unresolved(
                    IrUnknownReasons.UnresolvedTable,
                    $"{name.BaseIdentifier.Value} is not schema-qualified, and {string.Join(" and ", candidates.Select(c => $"the {c.Kind} {c.Name}"))} have that name: which is read depends on the caller's default schema.");
            }

            objectName = candidates.Count == 1 ? candidates[0].Name : ObjectName.From(name);
        }

        if (catalog.Change(objectName) is { } change)
            return new Source(alias, null, change, outerJoin);

        if (catalog.OtherKind(objectName) is { } kind)
            return Unresolved(IrUnknownReasons.UnresolvedTable, $"{objectName} is a {kind}, which is not modelled yet.");

        var table = catalog.Table(objectName);
        return table is not null
            ? new Source(alias, table, null, outerJoin)
            : Unresolved(IrUnknownReasons.UnresolvedTable, $"There is no table {objectName} in the project.");
    }

    /// <summary>
    /// Adds an unresolvable source for every alias under <paramref name="reference"/>, so that a column
    /// qualified by any of them, or not qualified at all, becomes unknown rather than resolving elsewhere.
    /// </summary>
    private static void AddUnresolved(TableReference reference, Scope scope, UnknownShape why)
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
                scope.Sources.Add(new Source(named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value, null, why, null));
                break;
            default:
                scope.Sources.Add(new Source((reference as TableReferenceWithAlias)?.Alias?.Value, null, why, null));
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

    // ---- expressions --------------------------------------------------------------------------------

    private Value Expression(ScalarExpression expression, Scope scope) => expression switch
    {
        ColumnReferenceExpression column => Column(column, scope),
        ParenthesisExpression parenthesis => Expression(parenthesis.Expression, scope),
        ScalarSubquery subquery => Subquery(subquery, scope),
        FunctionCall { CallTarget: null, Parameters.Count: 1 } call
            when call.FunctionName.Value.Equals("json_query", StringComparison.OrdinalIgnoreCase) => JsonQuery(call, scope),
        _ => Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The expression {Text(expression)} is not modelled yet.", At(expression))),
    };

    private Value Column(ColumnReferenceExpression column, Scope scope)
    {
        var at = At(column);
        if (column.ColumnType != ColumnType.Regular || column.MultiPartIdentifier is null)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The column reference {Text(column)} is not modelled yet.", at));

        var parts = column.MultiPartIdentifier.Identifiers;
        if (parts.Count > 2)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The multi-part column reference {Text(column)} is not modelled yet.", at));

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
                return Value.Unknown(Unsupported(IrUnknownReasons.UnresolvedColumn, $"The column {name} is ambiguous: more than one table in scope has it.", at));
            if (matches.Count == 1)
                return FromSource(matches[0], name, at);

            // A source that cannot be resolved may be the one that has this column.
            if (s.Sources.FirstOrDefault(x => x.Table is null) is { } unresolved)
                return FromSource(unresolved, name, at);
        }

        return Value.Unknown(Unsupported(IrUnknownReasons.UnresolvedColumn, $"No table in scope has the column {Text(column)}.", at));
    }

    private static Value FromSource(Source source, string name, SourceLocation at)
    {
        if (source.Table is null)
            return Value.Unknown(source.Unresolved! with { At = at });

        if (source.Table.Column(name) is not { } column)
            return Value.Unknown(new UnknownShape(IrUnknownReasons.UnresolvedColumn, $"The table {source.Table.Name} has no column {name}.", at));

        var because = new List<IrNullReason>();
        if (!column.NotNull)
            because.Add(new IrNullReason(IrNullRules.NullableColumn, $"{source.Table.Name}.{column.Name}"));
        if (source.OuterJoin is not null)
            because.Add(source.OuterJoin);
        return new Value(column.Type, because);
    }

    private Value Subquery(ScalarSubquery subquery, Scope scope)
    {
        var at = At(subquery);
        if (subquery.QueryExpression is not QuerySpecification { ForClause: JsonForClause json } query)
            return Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"A subquery without FOR JSON is not modelled yet: {Text(subquery)}", at));

        // A FOR JSON subquery is JSON that an enclosing FOR JSON embeds without escaping. It is null when the
        // subquery returns no rows, which nothing here can rule out.
        var shape = ForJson(Query(query, scope), json, at);
        return shape is UnknownShape unknown
            ? Value.Unknown(unknown)
            : new Value(shape, [new IrNullReason(IrNullRules.EmptySubquery, at.ToString())], IsLiveJson: true);
    }

    private Value JsonQuery(FunctionCall call, Scope scope)
    {
        var argument = Expression(call.Parameters[0], scope);
        if (argument.IsLiveJson || argument.Shape is UnknownShape)
            return argument;

        // json_query over text (a column, a variable) makes it live JSON, but its shape is in data we cannot see.
        return Value.Unknown(Unsupported(IrUnknownReasons.JsonShapeUnknown, $"{Text(call)} reads JSON that is not built here, so its shape is unknown.", At(call)));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static UnknownShape Unsupported(string reason, string detail, SourceLocation at) => new(reason, detail, at);

    private SourceLocation At(TSqlFragment fragment) => new(file, fragment.StartLine, fragment.StartColumn);

    private static string Text(SourceLocation at) => $"at {at}";

    /// <summary>The fragment's source text on one line, shortened, for messages.</summary>
    private static string Text(TSqlFragment fragment)
    {
        var text = new StringBuilder();
        for (var i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex; i++)
        {
            var token = fragment.ScriptTokenStream[i];
            if (token.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                if (text.Length > 0 && text[^1] != ' ')
                    text.Append(' ');
                continue;
            }

            text.Append(token.Text);
        }

        var result = text.ToString().Trim();
        return result.Length <= 80 ? result : result[..77] + "...";
    }
}
