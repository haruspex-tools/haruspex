using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a query file's uses of its parameters say their types are: the uses SQL Server's own <c>sp_describe_undeclared_parameters</c>
/// decides a type from, each by the server's rule (<see cref="ParameterRules"/>), read while the file is walked with its
/// parameters undeclared. The typer hands over each query it reads, and the walker each statement, in the scope it is read in, so
/// a column is typed where it is bound: in a CTE, a derived table, a temporary table.
/// </summary>
/// <remarks>
/// <para>
/// Only a use with the parameter itself as an operand decides anything, and only against a column or a literal, a procedure's or a
/// function's parameter, or a place that takes a count: what the server decides there is pinned by an observation each
/// (<c>QueryParameterObservationTests</c>). A use inside a larger expression is passed over, since the server types it from the
/// expression's context, by rules not modelled here: <c>total = @x * 2</c> makes <c>@x</c> <c>decimal(38,19)</c>, not
/// <c>int</c>. So is a use in a query the walk doesn't read, such as a subquery under <c>EXISTS</c>. Passing a use over claims
/// nothing: a parameter no use decides is unknown, and declared in the file instead.
/// </para>
/// <para>
/// The walk may read a query or a statement more than once (a loop, until it settles), so a use is kept once, by where it is.
/// <c>docs/decisions/0059-a-query-file-s-parameters-are-typed-from-their-uses.md</c> gives the design.
/// </para>
/// </remarks>
/// <param name="model">The model the file is read against, for the procedures and functions it calls.</param>
/// <param name="source">The file, whose positions every use names.</param>
/// <param name="binder">How the file's names are bound.</param>
internal sealed class ParameterUses(DatabaseModel model, SourceSpan source, Binder binder)
{
    /// <summary>
    /// Each use, once, by where the parameter is and how it is used there: one place may be several uses, as <c>@p IN (1, 2.5)</c>
    /// compares <c>@p</c> with each value, and each must agree for the type to be decided.
    /// </summary>
    private readonly Dictionary<(SourceLocation At, string How), ParameterUse> found = [];

    /// <summary>One use that decides a parameter's type: the parameter, the type it takes there, how it is used, and where.</summary>
    internal sealed record ParameterUse(LocalName Name, ScalarType Type, string How, SourceLocation At);

    /// <summary>Every use found, in the order of the text.</summary>
    public IReadOnlyList<ParameterUse> Found =>
        [.. found.Values.OrderBy(u => u.At.Line).ThenBy(u => u.At.Column)];

    // ---- where uses are read -----------------------------------------------------------------------------------------

    /// <summary>
    /// The uses in a query's own parts, read in its scope: its filters, its select list, its ORDER BY, TOP and OFFSET, and the calls
    /// in them. A subquery's own parts are read when the typer reads it, in its own scope.
    /// </summary>
    public void Specification(QuerySpecification query, ReadScope scope, Typer typer)
    {
        Condition(query.WhereClause?.SearchCondition, scope, typer);
        Condition(query.HavingClause?.SearchCondition, scope, typer);
        foreach (var reference in query.FromClause?.TableReferences ?? [])
            Joins(reference, scope, typer);

        // Arithmetic decides only where nothing around it does: a select list's column, or an ORDER BY's key.
        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
            Arithmetic(element.Expression, scope, typer);
        foreach (var element in query.OrderByClause?.OrderByElements ?? [])
            Arithmetic(element.Expression, scope, typer);

        Count(query.TopRowFilter?.Expression, "TOP", scope, query.TopRowFilter?.Percent == true);
        Count(query.OffsetClause?.OffsetExpression, "OFFSET", scope);
        Count(query.OffsetClause?.FetchExpression, "FETCH", scope);
        Calls(query, scope);
    }

    /// <summary>
    /// The uses a statement makes outside its queries, read in the scope it starts in: a condition of IF or WHILE, an UPDATE's
    /// SET and WHERE, a DELETE's WHERE, an INSERT's VALUES, an EXEC's arguments.
    /// </summary>
    public void Statement(TSqlStatement statement, ReadScope at, Typer typer)
    {
        switch (statement)
        {
            case IfStatement branch:
                Condition(branch.Predicate, at, typer);
                Calls(branch.Predicate, at);
                break;
            case WhileStatement loop:
                Condition(loop.Predicate, at, typer);
                Calls(loop.Predicate, at);
                break;
            case UpdateStatement { UpdateSpecification: var update } updating:
                typer.WithCtes(updating.WithCtesAndXmlNamespaces, at, scope =>
                {
                    var bound = typer.Attach(scope.In(binder.BindUpdate(update, scope.Names).Scope));
                    Condition(update.WhereClause?.SearchCondition, bound, typer);
                    foreach (var reference in update.FromClause?.TableReferences ?? [])
                        Joins(reference, bound, typer);
                    foreach (var set in update.SetClauses.OfType<AssignmentSetClause>())
                    {
                        if (set.Column is { } column && Parameter(set.NewValue, bound) is { } parameter)
                            Assigned(parameter, column, bound, typer);
                    }

                    Calls(update, bound);
                    return true;
                }, _ => false);
                break;
            case DeleteStatement { DeleteSpecification: var delete } deleting:
                typer.WithCtes(deleting.WithCtesAndXmlNamespaces, at, scope =>
                {
                    // Bound as an UPDATE's scope is: a query over its FROM, or over its target alone, filtered by its WHERE.
                    var query = new QuerySpecification { FromClause = delete.FromClause ?? new FromClause { TableReferences = { delete.Target } }, WhereClause = delete.WhereClause };
                    var bound = typer.Attach(scope.In(((BoundSpecification)binder.Bind(query, null, scope.Names)).Scope));
                    Condition(delete.WhereClause?.SearchCondition, bound, typer);
                    foreach (var reference in delete.FromClause?.TableReferences ?? [])
                        Joins(reference, bound, typer);
                    Calls(delete, bound);
                    return true;
                }, _ => false);
                break;
            case InsertStatement { InsertSpecification: { InsertSource: ValuesInsertSource values } insert } when insert.Columns.Count > 0:
                Inserted(insert, values, at, typer);
                break;
            case ExecuteStatement { ExecuteSpecification: var execute }:
                Arguments(execute, at);
                break;
        }
    }

    // ---- the uses ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The comparisons in a condition, through AND, OR, NOT and parentheses: each with a parameter as one operand, against a
    /// column or a literal as the other, whatever the truth of the whole.
    /// </summary>
    private void Condition(BooleanExpression? condition, ReadScope scope, Typer typer)
    {
        switch (condition)
        {
            case BooleanBinaryExpression both:
                Condition(both.FirstExpression, scope, typer);
                Condition(both.SecondExpression, scope, typer);
                break;
            case BooleanNotExpression not:
                Condition(not.Expression, scope, typer);
                break;
            case BooleanParenthesisExpression parenthesis:
                Condition(parenthesis.Expression, scope, typer);
                break;
            case BooleanComparisonExpression comparison:
                var how = ParameterRules.IsEquality(comparison.ComparisonType) ? ParameterRules.Comparison.Equality : ParameterRules.Comparison.Ordering;
                Compared(comparison.FirstExpression, comparison.SecondExpression, how, scope, typer);
                Compared(comparison.SecondExpression, comparison.FirstExpression, how, scope, typer);
                break;
            case InPredicate { Subquery: null } list:
                foreach (var value in list.Values)
                {
                    Compared(list.Expression, value, ParameterRules.Comparison.Equality, scope, typer);
                    Compared(value, list.Expression, ParameterRules.Comparison.Equality, scope, typer);
                }

                break;
            case BooleanTernaryExpression between:
                Compared(between.FirstExpression, between.SecondExpression, ParameterRules.Comparison.Ordering, scope, typer);
                Compared(between.FirstExpression, between.ThirdExpression, ParameterRules.Comparison.Ordering, scope, typer);
                Compared(between.SecondExpression, between.FirstExpression, ParameterRules.Comparison.Ordering, scope, typer);
                Compared(between.ThirdExpression, between.FirstExpression, ParameterRules.Comparison.Ordering, scope, typer);
                break;
            case LikePredicate like:
                Compared(like.FirstExpression, like.SecondExpression, ParameterRules.Comparison.Like, scope, typer);
                Compared(like.SecondExpression, like.FirstExpression, ParameterRules.Comparison.Like, scope, typer);
                break;
        }
    }

    /// <summary>The ON conditions of a FROM clause's joins, but not those inside a derived table, which is a query of its own.</summary>
    private void Joins(TableReference reference, ReadScope scope, Typer typer)
    {
        switch (reference)
        {
            case QualifiedJoin join:
                Joins(join.FirstTableReference, scope, typer);
                Joins(join.SecondTableReference, scope, typer);
                Condition(join.SearchCondition, scope, typer);
                break;
            case JoinTableReference join:
                Joins(join.FirstTableReference, scope, typer);
                Joins(join.SecondTableReference, scope, typer);
                break;
            case JoinParenthesisTableReference parenthesis:
                Joins(parenthesis.Join, scope, typer);
                break;
        }
    }

    /// <summary>A parameter compared with an operand: the operand's type, by the comparison's rule, when the operand is a column or a literal.</summary>
    private void Compared(ScalarExpression one, ScalarExpression other, ParameterRules.Comparison how, ReadScope scope, Typer typer)
    {
        if (Parameter(one, scope) is not { } parameter || !IsColumnOrLiteral(other) || Typed(other, scope, typer) is not { } type)
            return;
        if (ParameterRules.Compared(type, how) is { } taken)
            Add(parameter, taken, $"compared with {FragmentText.OneLine(other)}");
    }

    /// <summary>
    /// A parameter that is an operand of arithmetic whose other operand is a column or a literal, where the arithmetic is the
    /// whole of <paramref name="expression"/>.
    /// </summary>
    private void Arithmetic(ScalarExpression expression, ReadScope scope, Typer typer)
    {
        if (Strip(expression) is not BinaryExpression binary)
            return;
        foreach (var (one, other) in new[] { (binary.FirstExpression, binary.SecondExpression), (binary.SecondExpression, binary.FirstExpression) })
        {
            if (Parameter(one, scope) is { } parameter && IsColumnOrLiteral(other) && Typed(other, scope, typer) is { } type
                && ParameterRules.Arithmetic(type, binary.BinaryExpressionType) is { } taken)
            {
                Add(parameter, taken, $"in {FragmentText.OneLine(binary)}");
            }
        }
    }

    /// <summary>
    /// A parameter that is a count of rows: TOP's, OFFSET's or FETCH's, which the server takes as <c>bigint</c>; or TOP's
    /// <c>PERCENT</c>, which it takes as <c>float</c> (observed).
    /// </summary>
    private void Count(ScalarExpression? expression, string clause, ReadScope scope, bool percent = false)
    {
        if (Parameter(expression, scope) is { } parameter)
            Add(parameter, percent ? ParameterRules.Percent : ParameterRules.RowCount, $"the {(percent ? "percentage" : "count")} of {clause}");
    }

    /// <summary>A parameter assigned to a column, as UPDATE's SET does: the column's type.</summary>
    private void Assigned(Used parameter, ColumnReferenceExpression column, ReadScope scope, Typer typer)
    {
        if (Typed(column, scope, typer) is { } type)
            Add(parameter, type, $"assigned to {FragmentText.OneLine(column)}");
    }

    /// <summary>The parameters an INSERT's VALUES put in the columns it lists: each takes its column's type.</summary>
    private void Inserted(InsertSpecification insert, ValuesInsertSource values, ReadScope at, Typer typer)
    {
        if (insert.Target is not (NamedTableReference or VariableTableReference))
            return;

        // The columns are typed as a query over the target alone would read them, which binds them as the INSERT does.
        var query = new QuerySpecification { FromClause = new FromClause { TableReferences = { insert.Target } } };
        var scope = typer.Attach(at.In(((BoundSpecification)binder.Bind(query, null, at.Names)).Scope));
        foreach (var row in values.RowValues.Where(r => r.ColumnValues.Count == insert.Columns.Count))
        {
            foreach (var (column, value) in insert.Columns.Zip(row.ColumnValues))
            {
                if (Parameter(value, at) is { } parameter)
                    Assigned(parameter, column, scope, typer);
            }
        }
    }

    /// <summary>The parameters an EXEC passes to a procedure of the project: each takes the type of the parameter it is passed to.</summary>
    private void Arguments(ExecuteSpecification execute, ReadScope at)
    {
        if (binder.BindCall(execute) is not ProcedureCall { Procedure: var callee } || execute.ExecutableEntity is not ExecutableProcedureReference call)
            return;
        foreach (var (value, target) in ProcedureArguments.Matched(call, callee))
        {
            if (Parameter(value, at) is { } parameter)
                Passed(parameter, target, callee.Name);
        }
    }

    /// <summary>
    /// The parameters passed to the scalar and table-valued functions of the project that a fragment calls, outside the
    /// subqueries in it: each takes the type of the function's parameter.
    /// </summary>
    private void Calls(TSqlFragment fragment, ReadScope scope)
    {
        var calls = new CallFinder(fragment);
        fragment.Accept(calls);
        foreach (var (name, arguments) in calls.Found)
        {
            if (model.FindFunction(name) is not { } function)
                continue;
            foreach (var (argument, target) in arguments.Zip(function.Parameters))
            {
                if (Parameter(argument, scope) is { } parameter)
                    Passed(parameter, target, function.Name);
            }
        }
    }

    private void Passed(Used parameter, Parameter target, ObjectName callee)
    {
        if (SchemaRules.ResolveType(model, target.Type)?.Scalar is { } type)
            Add(parameter, type, $"passed to {target.Name} of {callee}");
    }

    private void Add(Used parameter, ScalarType type, string how)
    {
        var at = source.At(parameter.At);
        found.TryAdd((at, how), new ParameterUse(parameter.Name, type, how, at));
    }

    // ---- what an operand is ------------------------------------------------------------------------------------------

    /// <summary>The operand as a parameter of the file, a variable it doesn't declare, with its name; null for anything else.</summary>
    private static Used? Parameter(ScalarExpression? operand, ReadScope scope) =>
        operand is not null && Strip(operand) is VariableReference variable && Binder.Resolve(variable, scope.Names) is UndeclaredVariable undeclared
            ? new Used(undeclared.Name, variable)
            : null;

    /// <summary>A use of a parameter: its name, as binding resolved it, and where it is.</summary>
    private sealed record Used(LocalName Name, VariableReference At);

    /// <summary>
    /// Whether an operand is one whose type the server reads as the operand's own: a column, or a literal (a number's sign
    /// included), never an expression, whose type the server may take from the parameter's side.
    /// </summary>
    private static bool IsColumnOrLiteral(ScalarExpression operand) => Strip(operand) switch
    {
        ColumnReferenceExpression { ColumnType: ColumnType.Regular } => true,
        Literal and not NullLiteral => true,
        UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: var inner } => Strip(inner) is IntegerLiteral or NumericLiteral or RealLiteral or MoneyLiteral,
        _ => false,
    };

    /// <summary>The operand's built-in type with its arguments, as the typer reads it in <paramref name="scope"/>; null when it isn't one.</summary>
    private static ScalarType? Typed(ScalarExpression operand, ReadScope scope, Typer typer) =>
        typer.Expression(operand, scope).Shape is ScalarShape { BuiltIn: { } type } ? type : null;

    private static ScalarExpression Strip(ScalarExpression expression) =>
        expression is ParenthesisExpression parenthesis ? Strip(parenthesis.Expression) : expression;

    /// <summary>
    /// The calls of user functions in a fragment, with their arguments, but none inside a subquery or a nested statement: a
    /// scalar function's call (<c>shop.discount(@total, 0.1)</c>) and a table-valued function read as a source.
    /// </summary>
    private sealed class CallFinder(TSqlFragment root) : TSqlFragmentVisitor
    {
        public List<(ObjectName Name, IList<ScalarExpression> Arguments)> Found { get; } = [];

        public override void Visit(FunctionCall node)
        {
            if (FunctionSummaries.Callee(node) is { } name)
                Found.Add((name, node.Parameters));
        }

        public override void Visit(SchemaObjectFunctionTableReference node) =>
            Found.Add((SyntaxReader.Name(node.SchemaObject), node.Parameters));

        /// <summary>Only the root's own query: a subquery or a derived table is read when the typer reads it.</summary>
        public override void ExplicitVisit(QuerySpecification node)
        {
            if (ReferenceEquals(node, root))
                base.ExplicitVisit(node);
        }
    }
}
