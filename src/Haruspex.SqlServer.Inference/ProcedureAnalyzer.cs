using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
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
/// <remarks>
/// Which table and column a reference reads is the <see cref="Binder"/>'s job (Haruspex.SqlServer.Semantics);
/// what that means for a value's shape and nullability is decided here. Slice 2b split the two out of this one
/// class, so that lint, lineage and diff can bind without inferring.
/// </remarks>
internal sealed class ProcedureAnalyzer
{
    private readonly Binder binder;
    private readonly string file;

    public ProcedureAnalyzer(DatabaseModel model, string file)
    {
        binder = new Binder(model, file);
        this.file = file;
    }

    public ProcedureAnalysis Analyse(ProcedureStatementBody procedure)
    {
        var parameters = procedure.Parameters
            .Select(p => new ParameterShape(
                p.VariableName.Value,
                SqlTypes.ToShape(SqlType.From(p.DataType), At(p)),
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

    private Row Query(QuerySpecification query, BindingScope? parent)
    {
        var scope = binder.BindFrom(query, parent);

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

            // A nested object whose every value is null is left out of the output. With INCLUDE_NULL_VALUES it is
            // always written, with each key null, however deep it is nested: {"a":{"b":{"y":null}}}. Both were
            // observed on SQL Server 2025 and are kept as tests (ForJsonObservationTests, differential tests).
            var allNull = !includeNulls && nested.Fields.All(f => f.NullableBecause.Count > 0);
            fields.Add(allNull
                ? new FieldShape(key, nested, IrPresence.OmittedWhenNull, [new IrNullReason(IrNullRules.AllFieldsNull, at.ToString())])
                : new FieldShape(key, nested, IrPresence.Always, []));
        }

        return new ObjectShape(fields);
    }

    // ---- expressions --------------------------------------------------------------------------------

    private Value Expression(ScalarExpression expression, BindingScope scope) => expression switch
    {
        ColumnReferenceExpression column => Column(column, scope),
        ParenthesisExpression parenthesis => Expression(parenthesis.Expression, scope),
        ScalarSubquery subquery => Subquery(subquery, scope),
        FunctionCall { CallTarget: null, Parameters.Count: 1 } call
            when call.FunctionName.Value.Equals("json_query", StringComparison.OrdinalIgnoreCase) => JsonQuery(call, scope),
        _ => Value.Unknown(Unsupported(IrUnknownReasons.UnsupportedExpression, $"The expression {Text(expression)} is not modelled yet.", At(expression))),
    };

    private Value Column(ColumnReferenceExpression column, BindingScope scope) => binder.BindColumn(column, scope) switch
    {
        BoundColumn bound => FromColumn(bound),
        UnboundColumn unbound => Value.Unknown(Unknown(unbound.Problem)),
        _ => throw new InvalidOperationException("Unknown binding."),
    };

    private static Value FromColumn(BoundColumn bound)
    {
        var (source, table, column) = bound;
        var because = new List<IrNullReason>();
        if (!SchemaRules.IsNeverNull(table, column))
            because.Add(new IrNullReason(IrNullRules.NullableColumn, $"{table.Name}.{column.Name}"));

        // The optional side of an outer join has no row when nothing matches, so every column read from it can
        // be null, whatever its declaration says.
        if (source.OptionalSideOf is { } optional)
            because.Add(new IrNullReason(IrNullRules.OuterJoin, optional.ToString()));
        return new Value(ColumnShape(table, column), because);
    }

    /// <summary>A column's shape when it is not null.</summary>
    private static Shape ColumnShape(Table table, Column column) => column.IsComputed
        ? new UnknownShape(IrUnknownReasons.UnsupportedSqlType, $"{table.Name}.{column.Name} is a computed column, which is not modelled yet.", column.At)
        : SqlTypes.ToShape(column.Type, column.At);

    /// <summary>
    /// The IR's reason for a binding problem. A table changed after its definition was reported as an unsupported
    /// source before binding had a layer of its own, and still is; a reference form the binder does not model is
    /// an unsupported expression.
    /// </summary>
    private static UnknownShape Unknown(BindingProblem problem) => new(
        problem.Kind switch
        {
            BindingProblemKind.UnsupportedSource or BindingProblemKind.TableRedefined => IrUnknownReasons.UnsupportedSource,
            BindingProblemKind.UnresolvedTable => IrUnknownReasons.UnresolvedTable,
            BindingProblemKind.UnresolvedColumn => IrUnknownReasons.UnresolvedColumn,
            BindingProblemKind.UnsupportedReference => IrUnknownReasons.UnsupportedExpression,
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem.Kind, "Unknown binding problem."),
        },
        problem.Detail,
        problem.At);

    private Value Subquery(ScalarSubquery subquery, BindingScope scope)
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

    private Value JsonQuery(FunctionCall call, BindingScope scope)
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

    private static string Text(TSqlFragment fragment) => FragmentText.OneLine(fragment);
}
