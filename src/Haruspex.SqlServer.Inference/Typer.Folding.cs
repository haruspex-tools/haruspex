using System.Globalization;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// What SQL Server folds to a constant, where the answer changes a type. Constant folding (rewriting an expression whose value
// the compiler can work out into that value) happens twice, with different reach, and each changes something different:
//
// - When the server derives a column's type, it counts an int it folds to a literal by the literal's own digits beside a
//   decimal. It folds only expressions over literals; a column (even a derived table's literal), a variable, a parameter or
//   a subquery is never folded there, and OPTION (RECOMPILE) changes nothing.
// - When it decides whether a value nests as JSON or as escaped text, the plan matters: a derived table's json_query of a
//   literal is folded through, and with OPTION (RECOMPILE) a parameter's value is too. Only JSON built from a table's rows,
//   and conditions on a table's columns, are never folded there.
//
// Each rule below was observed at every compatibility level from 130 to 170, with and without OPTION (RECOMPILE)
// (FoldingObservationTests in the differential tests). README.md in this folder, "What the server folds", explains them, and
// decision 0088 says why only these are modelled.
internal sealed partial class Typer
{
    // ---- when the server derives a type ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether the server never folds <paramref name="expression"/> to a constant when it derives a type: a column of any
    /// source (a derived table's literal column too), a variable or parameter, or a subquery (observed:
    /// <c>Only_expressions_over_literals_are_folded_when_a_type_is_derived</c>). Anything else may be.
    /// </summary>
    private static bool NeverConstant(ScalarExpression expression) =>
        Strip(expression) is ColumnReferenceExpression { ColumnType: ColumnType.Regular } or VariableReference or ScalarSubquery;

    /// <summary>
    /// Whether the server never folds <paramref name="condition"/> to TRUE or FALSE when it derives a type: a comparison,
    /// <c>IS NULL</c>, <c>IN</c>, <c>LIKE</c> or <c>BETWEEN</c> over a value never constant and literals other than NULL, or
    /// <c>NOT</c>, <c>AND</c> and <c>OR</c> of such conditions, even a contradiction (observed:
    /// <c>A_condition_on_a_column_is_never_folded_but_one_with_a_constant_or_null_may_be</c>). A comparison with NULL, and
    /// <c>OR 1 = 1</c>, are folded.
    /// </summary>
    private static bool NeverConstant(BooleanExpression condition) => condition switch
    {
        BooleanParenthesisExpression parenthesis => NeverConstant(parenthesis.Expression),
        BooleanNotExpression not => NeverConstant(not.Expression),
        BooleanBinaryExpression binary => NeverConstant(binary.FirstExpression) && NeverConstant(binary.SecondExpression),
        BooleanComparisonExpression comparison => Operands(comparison.FirstExpression, comparison.SecondExpression),
        BooleanIsNullExpression isNull => NeverConstant(isNull.Expression),
        InPredicate { Subquery: null } inList => NeverConstant(inList.Expression) && inList.Values.All(NotNullOperand),
        LikePredicate like => NeverConstant(like.FirstExpression) && NotNullOperand(like.SecondExpression) && (like.EscapeExpression is null || NotNullOperand(like.EscapeExpression)),
        BooleanTernaryExpression between => NeverConstant(between.FirstExpression) && NotNullOperand(between.SecondExpression) && NotNullOperand(between.ThirdExpression),
        _ => false,
    };

    private static bool Operands(ScalarExpression first, ScalarExpression second) =>
        NotNullOperand(first) && NotNullOperand(second) && (NeverConstant(first) || NeverConstant(second));

    /// <summary>A value never constant, or a literal other than NULL: an operand that can't make a condition fold.</summary>
    private static bool NotNullOperand(ScalarExpression expression) =>
        NeverConstant(expression) || (LiteralOf(expression) is ({ } literal, _) && literal is not NullLiteral);

    /// <summary>
    /// Whether the server never folds a CASE or IIF to one of its results when it derives a type: its first condition is never
    /// constant, so which result is taken is known only per row (observed:
    /// <c>Only_expressions_over_literals_are_folded_when_a_type_is_derived</c>; a later constant condition doesn't fold it).
    /// </summary>
    private static bool NeverFoldsToOneResult(TSqlFragment at) => at switch
    {
        SearchedCaseExpression searched => NeverConstant(searched.WhenClauses[0].WhenExpression),
        SimpleCaseExpression simple => NeverConstant(simple.InputExpression),
        IIfCall iif => NeverConstant(iif.Predicate),
        _ => false,
    };

    /// <summary>
    /// The digits the server counts in arithmetic for <c>NULLIF(a, b)</c> whose arguments are literals or such NULLIFs, which it
    /// folds, however deep, to <c>a</c>'s integer, or to NULL (one digit) when the two are equal or <c>a</c> folds to NULL; null
    /// for any other expression (observed: <c>Nullif_of_two_literals_is_folded_inside_arithmetic_but_not_beside_other_values</c>).
    /// </summary>
    private static int? FoldedNullIfDigits(ScalarExpression expression) =>
        Strip(expression) is NullIfExpression && Folded(expression) is { } folded ? (folded.Null ? 1 : folded.Digits) : null;

    /// <summary>
    /// The integer constant <paramref name="expression"/> folds to: an integer literal, signed, or a NULLIF of such constants;
    /// NULL when a NULLIF's arguments are equal or its first is NULL. Null when it is anything else.
    /// </summary>
    private static (bool Null, decimal Value, int Digits)? Folded(ScalarExpression expression)
    {
        if (IntegerLiteralOf(expression) is var (literal, negative) && NumberOf(expression) is { } number)
            return (false, number, IntDigits.CountOf(literal.Value, negative));
        if (Strip(expression) is not NullIfExpression nullIf || Folded(nullIf.FirstExpression) is not { } a)
            return null;
        if (a.Null)
            return a;

        // A string that is not a number fails the comparison (245), so no value comes back and either count holds.
        decimal? b;
        if (Folded(nullIf.SecondExpression) is { } inner)
            b = inner.Null ? null : inner.Value;
        else if (NumberOf(nullIf.SecondExpression) is { } value)
            b = value;
        else if (LiteralOf(nullIf.SecondExpression) is ({ } text and StringLiteral, false))
            b = decimal.TryParse(text.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        else
            return null;
        return b == a.Value ? (true, 0, 1) : a;
    }

    /// <summary>
    /// Whether <paramref name="expression"/> is a NULLIF over an integer literal that the server may fold in arithmetic but
    /// <see cref="Folded"/> can't work out: its second argument is not a literal or such a NULLIF, yet may be constant.
    /// </summary>
    private static bool NullIfMayFold(ScalarExpression expression) =>
        Strip(expression) is NullIfExpression nullIf && FoldedNullIfDigits(expression) is null && LiteralRooted(nullIf.FirstExpression)
        && !NeverConstant(nullIf.SecondExpression);

    private static bool LiteralRooted(ScalarExpression expression) =>
        IntegerLiteralOf(expression) is not null || (Strip(expression) is NullIfExpression nullIf && LiteralRooted(nullIf.FirstExpression));

    // ---- when the server decides whether a value nests as JSON ----------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="expression"/> is JSON the server builds from rows, which it never folds to a constant JSON
    /// value: a FOR JSON subquery, even without FROM; <c>json_arrayagg</c> or <c>json_objectagg</c>, even of a literal; a
    /// subquery of one of those, cast or converted to <c>nvarchar(max)</c>; <c>json_query</c> of one; and <c>json_object</c>
    /// or <c>json_array</c> of a value that is or of a stored column (observed:
    /// <c>Coalesce_over_json_built_from_rows_is_text_wherever_it_nests_at_every_level</c>). Each is <c>nvarchar(max)</c>: JSON
    /// of a shorter type stays JSON through COALESCE, CASE and NULLIF (observed:
    /// <c>Json_of_a_type_shorter_than_max_stays_json_through_case_and_coalesce</c>), so <c>json_query</c> of a column, whose type
    /// is the column's, is not one. A column of a derived table, CTE or view, a variable or a parameter may be folded (a derived
    /// table's <c>json_query(N'[1]')</c> is), so none is.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="scope">Where its columns are bound; null inside a subquery, whose columns are not read here.</param>
    private bool BuiltFromRows(ScalarExpression expression, ReadScope? scope) => Strip(expression) switch
    {
        ScalarSubquery subquery when ForOf(subquery.QueryExpression) is JsonForClause => true,
        ScalarSubquery { QueryExpression: QuerySpecification { SelectElements: [SelectScalarExpression only] } } => BuiltFromRows(only.Expression, null),
        CastCall cast => IsMaxString(cast.DataType) && BuiltFromRows(cast.Parameter, scope),
        ConvertCall { Style: null } convert => IsMaxString(convert.DataType) && BuiltFromRows(convert.Parameter, scope),
        FunctionCall { CallTarget: null, OverClause: null } call when BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Aggregate) is { } aggregate
            && (aggregate == AggregateFunctions.JsonArrayAgg || aggregate == AggregateFunctions.JsonObjectAgg) => true,
        FunctionCall { CallTarget: null, Parameters: [var inner] } call when BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.JsonQuery =>
            BuiltFromRows(inner, scope),
        FunctionCall { CallTarget: null } call when IsTextConstructor(call) =>
            call.JsonParameters.Select(p => p.JsonValue).Concat(call.Parameters).Any(v => BuiltFromRows(v, scope) || StoredColumn(v, scope)),
        _ => false,
    };

    private static bool IsMaxString(DataTypeReference type) =>
        type is SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.NVarChar or SqlDataTypeOption.VarChar, Parameters: [MaxLiteral] };

    /// <summary><c>json_object</c> or <c>json_array</c>, not <c>RETURNING json</c>: JSON of type <c>nvarchar(max)</c>.</summary>
    private static bool IsTextConstructor(FunctionCall call) =>
        call.CallTarget is null && call.ReturnType is not { Count: > 0 }
        && BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) is { } scalar
        && (scalar == ScalarFunctions.JsonObject || scalar == ScalarFunctions.JsonArray);

    /// <summary>
    /// Whether <paramref name="expression"/> is JSON of type <c>nvarchar(max)</c>, which makes a CASE over it text rather than
    /// JSON where it is never folded: JSON built from rows, <c>json_object</c> or <c>json_array</c>, or <c>json_query</c> of one.
    /// </summary>
    private bool MaxJson(ScalarExpression expression, ReadScope scope) => NeverFoldedAsJson(expression, scope)
        || (Strip(expression) is FunctionCall { CallTarget: null, Parameters: [var inner] } call
            && BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.JsonQuery && MaxJson(inner, scope));

    /// <summary>Whether <paramref name="expression"/> reads a stored column of a table, never a constant, nor computed from one.</summary>
    private bool StoredColumn(ScalarExpression expression, ReadScope? scope) =>
        scope is not null && Strip(expression) is ColumnReferenceExpression { ColumnType: ColumnType.Regular } column
        && binder.Resolve(column, scope.Bound) is TableColumn { Column.IsComputed: false };

    /// <summary>
    /// Whether <paramref name="expression"/> nests as text however the plan folds it, as the first argument of a COALESCE or a
    /// query of a UNION: JSON built from rows (<see cref="BuiltFromRows"/>), or a <c>json_object</c> or <c>json_array</c>,
    /// which come out as text even of constants (observed:
    /// <c>Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch</c>,
    /// <c>A_union_over_json_built_from_rows_is_text_in_every_row</c>).
    /// </summary>
    private bool NeverFoldedAsJson(ScalarExpression expression, ReadScope scope) =>
        BuiltFromRows(expression, scope) || (Strip(expression) is FunctionCall call && IsTextConstructor(call));

    /// <summary>
    /// Whether a CASE or IIF over JSON nests as text both in FOR JSON and in <c>json_object</c>, whatever the plan: every
    /// condition compares a stored column of a table with a literal other than NULL, or tests it for NULL, so the server never
    /// folds it to one result; one of its results is JSON of type <c>nvarchar(max)</c> (<see cref="MaxJson"/>), since of a
    /// shorter type it stays JSON; and its last result is absent, NULL, a string literal or <c>json_query</c> of one. In
    /// <c>json_object</c> a CASE whose last result is JSON it builds nests as JSON (observed:
    /// <c>A_case_over_json_on_a_tables_column_is_text_where_its_last_result_is_null_or_a_literal</c>), and a condition on a
    /// parameter is folded under OPTION (RECOMPILE), so neither is typed.
    /// </summary>
    private bool NestsAsText(TSqlFragment at, ReadScope scope) => at switch
    {
        SearchedCaseExpression searched => searched.WhenClauses.All(w => OnStoredColumns(w.WhenExpression, scope)) && LastResultConstant(searched.ElseExpression)
            && searched.WhenClauses.Select(w => w.ThenExpression).Append(searched.ElseExpression).OfType<ScalarExpression>().Any(r => MaxJson(r, scope)),
        IIfCall iif => OnStoredColumns(iif.Predicate, scope) && LastResultConstant(iif.ElseExpression)
            && (MaxJson(iif.ThenExpression, scope) || MaxJson(iif.ElseExpression, scope)),
        _ => false,
    };

    private bool OnStoredColumns(BooleanExpression condition, ReadScope scope) => condition switch
    {
        BooleanParenthesisExpression parenthesis => OnStoredColumns(parenthesis.Expression, scope),
        BooleanIsNullExpression isNull => StoredColumn(isNull.Expression, scope),
        BooleanComparisonExpression comparison =>
            (StoredColumn(comparison.FirstExpression, scope) && NotNullLiteral(comparison.SecondExpression))
            || (StoredColumn(comparison.SecondExpression, scope) && NotNullLiteral(comparison.FirstExpression)),
        _ => false,
    };

    private static bool NotNullLiteral(ScalarExpression expression) => LiteralOf(expression) is ({ } literal, _) && literal is not NullLiteral;

    /// <summary>
    /// Whether the last result of a CASE, IIF or COALESCE is absent, NULL, a string literal or <c>json_query</c> of one, so
    /// that <c>json_object</c> nests it as text: where the last is JSON built here, it nests as JSON (observed for both).
    /// </summary>
    private static bool LastResultConstant(ScalarExpression? last) => last is null || Strip(last) switch
    {
        NullLiteral or StringLiteral => true,
        FunctionCall { CallTarget: null, Parameters: [var inner] } call =>
            BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.JsonQuery && LiteralOf(inner) is ({ } and StringLiteral, false),
        _ => false,
    };
}
