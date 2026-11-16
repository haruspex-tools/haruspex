using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
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

    // ---- CAST and CONVERT of JSON -------------------------------------------------------------------------------------------
    //
    // Observed on SQL Server 2025 RTM-CU10 and kept as tests (JsonTracingObservationTests, "conversions"): live JSON converted
    // to its own type, or to json, is still JSON, which FOR JSON and the constructors nest unescaped; converted to any other
    // type, or with a style (even 0) to a string type, it is text, which they escape. So a FOR JSON subquery (nvarchar(max)) cast to nvarchar(max) stays JSON, while a json
    // value cast to nvarchar(max) becomes text holding it, and either cast to nvarchar(4000) is text cut short. JSON text built
    // WITHOUT_ARRAY_WRAPPER cast to json is JSON again.

    /// <summary>
    /// <c>CAST(x AS json)</c> and its forms, where x is JSON built here: live JSON of the same shape, null when x is. Live JSON
    /// stays live, and JSON text with known content is made live, as json_query makes it. Anything else is not modelled yet.
    /// The json type parses the text again, which writes a float as a number of 10 places (1.5e-11 as 0.0000000000) and refuses
    /// one of 1e38 or more (1007; observed: Json_converted_to_json_rewrites_a_float_as_a_decimal_of_ten_places). The shape still
    /// holds because a float in JSON claims only that it is a number, with no facets; a float facet would have to be dropped here.
    /// </summary>
    private Value JsonCast(ScalarExpression operand, TSqlFragment call, ReadScope scope)
    {
        var value = Expression(operand, scope);
        if (value.Shape is UnknownShape || value.IsLiveJson)
            return value;
        if (value.Shape is JsonTextShape { Content: { } content })
            return content is UnknownShape unknown ? Value.Unknown(unknown) : new Value(content, value.NullableBecause, IsLiveJson: true);
        return NotModelled(call, "a conversion to this type is not modelled yet.");
    }

    /// <summary>
    /// Live JSON converted to a type other than json: unchanged when the type is its own and no style is given; text holding it when the type is
    /// <c>nvarchar(max)</c>, which holds any JSON unchanged; a string of the type, cut or padded, for any other string type
    /// (and <c>varchar(max)</c>, whose code page may replace a character with <c>?</c>, so its content is not claimed); not
    /// modelled for any other type, or when its own type is not known and may be the target's.
    /// </summary>
    /// <param name="shape">The target type's shape.</param>
    /// <param name="styled">Whether CONVERT is given a style, which leaves the text as it is but makes it text.</param>
    private Value FromLiveJson(Value value, ScalarExpression operand, ScalarType to, Shape shape, TSqlFragment call, bool styled)
    {
        if (!to.IsString)
            return NotModelled(call, "a conversion of JSON is not modelled yet.");
        var own = styled ? null : LiveJsonType(operand);
        if (own?.Text == to.Text)
            return value;

        // Live JSON is nvarchar(max) (built), nvarchar(4000) (json_query over a shorter string, observed with
        // sp_describe_first_result_set) or json, so only the first two can be its own type when it isn't known.
        if (!styled && own is null && to.Type == BuiltInType.NVarChar && (to.IsMax || to.Length == 4000))
            return NotModelled(call, "whether converted JSON stays JSON depends on its own type, which isn't known here.");
        return SqlTypes.IsNVarCharMax((ScalarShape)shape)
            ? new Value(new JsonTextShape(value.Shape), value.NullableBecause)
            : Value.Computed(shape, value.NullableBecause);
    }

    /// <summary>
    /// The SQL type of live JSON, read from the expression that makes it: <c>nvarchar(max)</c> when built here as text
    /// (<see cref="BuiltAsText"/>), json for a constructor <c>RETURNING json</c>, the target of a conversion; null for anything
    /// else, json_query among them (its type follows its argument's).
    /// </summary>
    private ScalarType? LiveJsonType(ScalarExpression expression) => Strip(expression) switch
    {
        var built when BuiltAsText(built) => new ScalarType(BuiltInType.NVarChar, -1),
        FunctionCall { CallTarget: null } call when IsConstructor(call) => ScalarType.Of(BuiltInType.Json),
        CastCall cast => TargetType(cast.DataType),
        ConvertCall convert => TargetType(convert.DataType),
        TryCastCall cast => TargetType(cast.DataType),
        TryConvertCall convert => TargetType(convert.DataType),
        _ => null,
    };

    private ScalarType? TargetType(DataTypeReference target) => SchemaRules.ResolveType(model, SyntaxReader.Type(target))?.Scalar;
}
