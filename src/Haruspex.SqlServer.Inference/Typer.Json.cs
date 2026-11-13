using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// JSON built here, traced to where it is read. Every rule here was observed on SQL Server 2025 RTM-CU10 before it
// was relied on, and is kept as a test (JsonTracingObservationTests in the differential tests). See README.md, "JSON built
// here, traced".
internal sealed partial class Typer
{
    /// <summary>
    /// Whether a place of this declared value keeps JSON text written to it unchanged: <c>nvarchar(max)</c>. A shorter type cuts
    /// it short without an error, and <c>varchar</c> may change characters its code page lacks (observed), so neither is traced.
    /// </summary>
    internal static bool Traced(Shape declared) =>
        declared is ScalarShape scalar && SqlTypes.IsNVarCharMax(scalar);

    internal static bool Traced(Value declared) => !declared.IsLiveJson && Traced(declared.Shape);

    // ---- json_query ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// What <c>json_query</c>'s argument holds: a literal that is a JSON document its exact shape, as JSON text never null; a
    /// literal that is not, an unknown saying why (it is an error, 13609, observed); anything else its value, a CASE, IIF,
    /// CHOOSE or COALESCE read as JSON (<see cref="ReadScope.JsonQueryArgument"/>).
    /// </summary>
    internal Value JsonQueryArgument(ScalarExpression argument, ReadScope scope)
    {
        if (LiteralOf(argument) is ({ } literal and StringLiteral, false))
        {
            var parsed = JsonLiterals.Parse(literal.Value, literal is StringLiteral { IsNational: true }, At(literal));
            return parsed is UnknownShape unknown ? Value.Unknown(unknown) : new Value(new JsonTextShape(parsed), []);
        }

        return Expression(argument, scope.UnderJsonQuery(Strip(argument)));
    }

    /// <summary>
    /// Whether an expression is built JSON whose SQL type is <c>nvarchar(max)</c>, so that a column <c>SELECT … INTO</c> makes of
    /// it holds the JSON as text: a FOR JSON subquery, or a constructor not <c>RETURNING json</c>, which makes a <c>json</c> column
    /// (observed: Select_into_makes_nvarchar_max_of_built_json_and_json_of_returning_json). Anything else live, such as
    /// json_query, is not read (its type follows its argument's).
    /// </summary>
    internal static bool BuiltAsText(ScalarExpression expression) => Strip(expression) switch
    {
        ScalarSubquery subquery => ForOf(subquery.QueryExpression) is JsonForClause,
        FunctionCall { CallTarget: null } call => IsConstructor(call) && call.ReturnType is not { Count: > 0 },
        _ => false,
    };
}
