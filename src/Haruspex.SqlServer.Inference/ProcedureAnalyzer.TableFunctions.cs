using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Table-valued functions as table sources. README.md in this folder ("Table-valued functions") gives the rules and
// the observations they rest on (TableFunctionObservationTests in the differential tests).
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// A call of a table-valued function in FROM or APPLY: its arguments read where the call is (an APPLY's left input's
    /// columns too), converted to its parameters' types, and the function read with them (<see cref="ViewSummaries.Function"/>).
    /// </summary>
    private LocalRelationValues TableFunction(SchemaObjectFunctionTableReference call, BindingScope? outer)
    {
        var at = At(call);
        LocalRelationValues Fails(string reason, string detail) => LocalRelationValues.Unknown(Unsupported(reason, $"{Text(call)}: {detail}", at));

        // A column list at the call is refused for a function (observed: 317), and so are table hints (a syntax error).
        if (call.Columns.Count > 0)
            return Fails(IrUnknownReasons.UnsupportedSource, "a table-valued function cannot have a column alias (317).");
        var name = call.SchemaObject;
        if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            return Fails(IrUnknownReasons.UnsupportedSource, "a function of another database is not modelled yet.");

        // An unqualified name resolves in the caller's default schema and then in dbo, as a table's does (Binder): the one
        // function of that name when exactly one schema has it.
        UserFunction? function;
        if (name.SchemaIdentifier is not null)
        {
            function = model.FindFunction(ObjectName.From(name));
        }
        else
        {
            var candidates = model.Functions.Where(f => f.Name.Name.Equals(name.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count > 1)
                return Fails(IrUnknownReasons.UnresolvedTable, $"{name.BaseIdentifier.Value} is not schema-qualified, and {string.Join(" and ", candidates.Select(c => c.Name))} have that name.");
            function = candidates.FirstOrDefault();
        }

        if (function is null)
            return Fails(IrUnknownReasons.UnresolvedTable, "there is no such function in the project.");
        if (function.Redefinition is { } redefinition)
            return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReasons.Redefined, redefinition.Message, redefinition.At));
        if (function.Body.MethodSpecifier is not null)
            return Fails(IrUnknownReasons.ExternalCode, $"{function.Name} is a CLR function: its rows come from .NET code.");
        if (function.Body.ReturnType is not (SelectFunctionReturnType or TableValuedFunctionReturnType))
            return Fails(IrUnknownReasons.UnresolvedTable, $"{function.Name} is a scalar function, not a table.");

        var parameters = function.Body.Parameters;
        if (call.Parameters.Count != parameters.Count)
            return Fails(IrUnknownReasons.UnsupportedSource, $"{function.Name} takes {parameters.Count} arguments.");

        var scope = binder.BindFrom(new QuerySpecification(), outer, outer is null ? locals : null);
        var arguments = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Count; i++)
            arguments[parameters[i].VariableName.Value] = Argument(function, parameters[i], call.Parameters[i], scope);
        return views!.Function(function, arguments, ReadUncommitted);
    }

    /// <summary>
    /// What a parameter holds for one call: the argument, converted to the parameter's type as an assignment converts it (a
    /// string cut short, an overflow NULL under a caller's ANSI_WARNINGS and ARITHABORT off: observed,
    /// An_argument_is_converted_to_its_parameters_type_as_an_assignment_is), null exactly when the argument may be or the
    /// conversion may overflow. <c>DEFAULT</c> passes the parameter's default (observed: Default_passes_the_parameters_default).
    /// An argument of the parameter's own type keeps its facts. One not modelled is any value of the type, or null
    /// (<c>parameter</c>), as a scalar function's parameter is.
    /// </summary>
    private Value Argument(UserFunction function, ProcedureParameter parameter, ScalarExpression argument, BindingScope scope)
    {
        var at = new SourceLocation(function.File, parameter.StartLine, parameter.StartColumn);
        var declared = SqlTypes.ToConvertedShape(model, SqlType.From(parameter.DataType), at);
        var any = new Value(declared, [new IrNullReason(IrNullRules.Parameter, parameter.VariableName.Value)]);
        if (declared is UnknownShape unknown)
            return Value.Unknown(unknown);

        Value value;
        if (Strip(argument) is DefaultLiteral)
        {
            // The default is read where the function is defined; a constant reads nothing there.
            if (parameter.Value is not { } fallback)
                return any;
            value = Expression(fallback, binder.BindFrom(new QuerySpecification(), null));
        }
        else
        {
            value = Expression(argument, scope);
        }

        if (value.Shape is UnknownShape || value.IsLiveJson)
            return any;
        if (TypeOf(value, argument) is { } from && declared is ScalarShape { Type.Sql: var sql } && from.Text == sql)
            return value;
        var fits = LiteralOf(argument) is ({ } literal, var negative) && ScalarType.Parse((declared as ScalarShape)?.Type.Sql) is { } to
            ? ScalarType.Fits(literal, to, negative)
            : null;
        return new Value(declared, fits == true ? value.NullableBecause : Converting(value, declared, At(argument)));
    }

    /// <summary>
    /// An inline function's rows: its query read as a view's is (<see cref="ReadView"/>), in its own file and ANSI_NULLS, with
    /// its parameters holding the call's values, which the query reads as constants: a key compared with one is fixed. Its
    /// keys pass through as a view's do (<see cref="ViewKeys"/>).
    /// </summary>
    public LocalRelationValues ReadInline(UserFunction function, SelectStatement select)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(select);
        foreach (var parameter in function.Body.Parameters)
            variableTypes[parameter.VariableName.Value] = SqlType.From(parameter.DataType);
        locals = Locals(new Dictionary<string, LocalRelationValues>(StringComparer.OrdinalIgnoreCase));
        var at = At(select);
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReasons.UnsupportedQuery, $"{function.Name}: {detail}", at));

        return WithCtes(
            select.WithCtesAndXmlNamespaces,
            () =>
            {
                if (select.QueryExpression is not { } query || ForOf(query) is not null)
                    return Fails("a function whose query is FOR XML or FOR JSON is not modelled yet.");
                var row = Query(query, parent: null);
                return Relation([], row) is { } relation
                    ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One, Keys = Keys(query, row) }
                    : Fails("its columns cannot be listed: SELECT * is not expanded in a function, and every column needs one name of its own.");
            },
            LocalRelationValues.Unknown);
    }

    /// <summary>
    /// A multi-statement function's rows: its declared return table, whatever its body inserts. The table enforces its
    /// declaration (observed: A_multi_statement_functions_return_table_enforces_its_constraints): every value is converted to
    /// its column's type, a NULL into a NOT NULL column fails the call, and so does a row that breaks a PRIMARY KEY, a UNIQUE
    /// constraint or a CHECK. So its columns are the declared ones, with their CHECK facets, and its keys are its declared
    /// keys. How many rows it returns is not read.
    /// </summary>
    public LocalRelationValues ReadReturnTable(UserFunction function, TableValuedFunctionReturnType returns)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(returns);
        var name = function.Name.ToString();
        var table = Table.FromDefinition(new ObjectName("", returns.DeclareTableVariableBody.VariableName.Value), returns.DeclareTableVariableBody.Definition, file, At(returns));
        var values = table.Columns.Select(c => Declared(table, c, name, temporary: false)).ToList();
        var keys = new List<RelationKey>();
        foreach (var key in SchemaRules.EnforcedKeys(table))
        {
            var ordinals = key.Columns.Select(c => table.Columns.ToList().FindIndex(x => x.Name.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
            var types = key.Columns.Select(c => SchemaRules.ResolveType(model, table.Column(c)?.Type)?.BuiltIn.Name).ToList();
            if (ordinals.All(o => o >= 0) && types.All(t => t is not null))
                keys.Add(new RelationKey([.. ordinals], [.. types!]));
        }

        // What its nvarchar(max) columns hold as JSON is what its body writes to them: the body is walked as a
        // procedure's is, the return table a table variable, and its traced columns read as they stand where it returns. A body
        // the walk cannot follow leaves them as declared.
        var walked = Walk(function.Body, returns.DeclareTableVariableBody);
        if (walked.Blocker is null && walked.End is { } end)
        {
            var variable = returns.DeclareTableVariableBody.VariableName.Value;
            values = [.. values.Select((v, i) => end.Json.TryGetValue($"{variable}.{table.Columns[i].Name}", out var slot) ? slot.Read(v) : v)];
        }

        return new LocalRelationValues([.. table.Columns.Select(c => c.Name)], [.. values], null) { Keys = [.. keys] };
    }
}
