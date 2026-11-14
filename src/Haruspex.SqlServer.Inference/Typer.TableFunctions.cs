using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Typing;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Table-valued functions as table sources. README.md in this folder ("Table-valued functions") gives the rules and
// the observations they rest on (TableFunctionObservationTests in the differential tests).
internal sealed partial class Typer
{
    /// <summary>
    /// A call of a table-valued function in FROM or APPLY: its arguments read where the call is (an APPLY's left input's
    /// columns too), converted to its parameters' types, and the function read with them (<see cref="Summaries.TableFunction"/>).
    /// </summary>
    /// <param name="symbol">The call, bound: which function it names was resolved by binding.</param>
    /// <param name="scope">The scope its arguments are read in.</param>
    private LocalRelationValues TableFunction(TableFunctionCall symbol, ReadScope scope)
    {
        var call = symbol.Syntax;
        var at = At(call);
        LocalRelationValues Fails(IrUnknownReason reason, string detail) => LocalRelationValues.Unknown(Unsupported(reason, $"{Text(call)}: {detail}", at));

        // A column list at the call is refused for a function (observed: 317), and so are table hints (a syntax error).
        if (call.Columns.Count > 0)
            return Fails(IrUnknownReason.UnsupportedSource, "a table-valued function cannot have a column alias (317).");
        // Which function the call names, binding resolved; why none, it says, and the words for it are said here.
        return symbol.Callee switch
        {
            KnownCallee known => Rows(known.Function),
            ForeignCallee => Fails(IrUnknownReason.UnsupportedSource, "a function of another database is not modelled yet."),
            AmbiguousCallee ambiguous => Fails(IrUnknownReason.UnresolvedTable, $"{ambiguous.Name} is not schema-qualified, and {string.Join(" and ", ambiguous.Candidates.Select(c => c.Name))} have that name."),
            MissingCallee => Fails(IrUnknownReason.UnresolvedTable, "there is no such function in the project."),
        };

        LocalRelationValues Rows(Function function)
        {
            if (function.Redefinition is { } redefinition)
                return LocalRelationValues.Unknown(new UnknownShape(IrUnknownReason.Redefined, Redefinitions.Why(redefinition), redefinition.At));
            if (function is ScalarFunction { Body: ClrBody } or TableFunction { Body: ClrBody })
                return Fails(IrUnknownReason.ExternalCode, $"{function.Name} is a CLR function: its rows come from .NET code.");
            if (function is ScalarFunction)
                return Fails(IrUnknownReason.UnresolvedTable, $"{function.Name} is a scalar function, not a table.");

            var parameters = function.Parameters;
            if (call.Parameters.Count != parameters.Count)
                return Fails(IrUnknownReason.UnsupportedSource, $"{function.Name} takes {parameters.Count} arguments.");

            var arguments = parameters.Select((p, i) => KeyValuePair.Create(new LocalName(p.Name), Argument(p, call.Parameters[i], scope)));
            return summaries.TableFunction(function, [.. arguments], ReadUncommitted(scope));
        }
    }

    /// <summary>
    /// What a parameter holds for one call: the argument, converted to the parameter's type as an assignment converts it (a
    /// string cut short, an overflow NULL under a caller's ANSI_WARNINGS and ARITHABORT off: observed,
    /// An_argument_is_converted_to_its_parameters_type_as_an_assignment_is), null exactly when the argument may be or the
    /// conversion may overflow or round to fewer places (<see cref="Converting"/>). <c>DEFAULT</c> passes the parameter's default (observed: Default_passes_the_parameters_default).
    /// An argument of the parameter's own type keeps its facts. One not modelled is any value of the type, or null
    /// (<c>parameter</c>), as a scalar function's parameter is.
    /// </summary>
    private Value Argument(Parameter parameter, ScalarExpression argument, ReadScope scope)
    {
        var declared = SqlTypes.ToConvertedShape(model, parameter.Type, parameter.At);
        var any = Value.Computed(declared, [new IrNullReason(IrNullRule.Parameter, parameter.Name)]);
        if (declared is UnknownShape unknown)
            return Value.Unknown(unknown);

        Value value;
        if (Strip(argument) is DefaultLiteral)
        {
            // The default is read where the function is defined; a constant reads nothing there.
            if (parameter.Default is not { } fallback)
                return any;
            value = Expression(fallback, ReadScope.Statement(NameEnvironment.Empty, scope.State));
        }
        else
        {
            value = Expression(argument, scope);
        }

        if (value.Shape is UnknownShape || value.IsLiveJson)
            return any;
        var target = (declared as ScalarShape)?.BuiltIn;
        if (TypeOf(value) is { } from && target is not null && from == target)
            return value;
        var fits = LiteralOf(argument) is ({ } literal, var negative) && target is not null
            ? Conversions.Fits(literal, target, negative)
            : null;
        return Value.Computed(declared, Converting(value, declared, At(argument), summaries.Session, argument, fits: fits == true));
    }

    /// <summary>
    /// An inline function's rows: its query read as a view's is (<see cref="ReadView"/>), in its own file and ANSI_NULLS, with
    /// its parameters holding the call's values, which the query reads as constants: a key compared with one is fixed. Its
    /// keys pass through as a view's do (<see cref="ViewKeys"/>).
    /// </summary>
    public LocalRelationValues ReadInline(InlineFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var select = function.Query;
        var names = NameEnvironment.Empty.WithDeclarations(binder.Declarations(function.Parameters));
        var at = At(select);
        LocalRelationValues Fails(string detail) => LocalRelationValues.Unknown(Unsupported(IrUnknownReason.UnsupportedQuery, $"{function.Name}: {detail}", at));

        return WithCtes(
            select.WithCtesAndXmlNamespaces,
            ReadScope.Statement(names, FlowState.Start([])),
            scope =>
            {
                if (select.QueryExpression is not { } query || ForOf(query) is not null)
                    return Fails("a function whose query is FOR XML or FOR JSON is not modelled yet.");
                var bound = binder.Bind(query, null, scope.Names);
                var row = Query(bound, scope);
                return Holds(bound.Listed([]), row) is { } relation
                    ? relation with { Rows = row.Rows, AtLeastOne = row.Rows == IrCardinality.One, Keys = Keys(query, row, scope) }
                    : Fails("its columns cannot be listed: SELECT * is not expanded in a function, and every column needs one name of its own.");
            },
            LocalRelationValues.Unknown);
    }
}
