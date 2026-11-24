using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// Masked columns: what a caller without UNMASK reads of a masked column, and of any value computed from one. README.md in this
// folder ("Masked columns") gives the rules; the observations they rest on are MaskingObservationTests in the differential tests,
// and the decision is docs/decisions/0118-a-masked-column-claims-only-its-type-nullability-and-greatest-length.md.
internal sealed partial class Typer
{
    /// <summary>The masked values this typer's expressions have given (<see cref="MaskedIfFrom"/>).</summary>
    private readonly MaskTally masked = new();

    /// <summary>
    /// The value an expression gives, masked when it is computed from a masked value. The server masks the result of an expression
    /// whose value comes from a masked column, as a whole: <c>rating + 1</c> is <c>0</c>, <c>'[' + code + ']'</c> is <c>'xxx'</c>,
    /// <c>COUNT(rating)</c> is <c>0</c>, and so is a <c>CASE</c> that may give one (observed:
    /// <c>An_expression_computed_from_a_masked_column_is_masked_as_a_whole</c>). So instead of asking each rule to pass the fact on,
    /// every expression counts the masked values its operands gave, and is masked when any did. A column a condition reads makes
    /// no value of it: <c>CASE WHEN rating = 4 THEN 'a' ELSE 'b' END</c> gives <c>'a'</c> or <c>'b'</c> (observed:
    /// <c>A_condition_on_a_masked_column_leaves_the_value_it_chooses_unmasked</c>), and conditions are read as proofs, not typed as
    /// values. A rule that types a condition's operands would mask more than the server does, which only claims less.
    /// </summary>
    /// <param name="value">What the expression gives, as its rule typed it.</param>
    /// <param name="before">How many masked values had been given before the expression was typed.</param>
    /// <param name="expression">The expression.</param>
    private Value MaskedIfFrom(Value value, int before, ScalarExpression expression)
    {
        if (value.MaskedBy is { } by)
        {
            masked.Add(by);
            return value.Masked(by, At(expression));
        }

        if (masked.Count == before || masked.Last is not { } from)
            return value;
        masked.Add(from);
        return value.Masked(from, At(expression));
    }

    /// <summary>
    /// The masked column <paramref name="bound"/> is, as <c>schema.table.column</c>, when a caller reads its mask: a table's column
    /// declared masked, unless the callers are declared to hold <c>UNMASK</c> (<see cref="Summaries.MasksApply"/>). Only a table of
    /// the schema is bound to a <see cref="TableColumn"/>. A body's own temporary table is not masked to the session that made it,
    /// which owns it (observed: <c>A_masked_column_of_a_temporary_table_is_not_masked_to_the_session_that_made_it</c>), so its
    /// values are masked only as what was written to it was; a view's column is masked by what its query reads.
    /// </summary>
    private string? MaskOf(TableColumn bound) =>
        summaries.MasksApply && bound.Column.IsMasked ? $"{bound.Table.Name}.{bound.Column.Name}" : null;

    /// <summary>
    /// Why a top-level <c>FOR JSON</c> query's text is not JSON for a caller without <c>UNMASK</c>: one of its values is JSON computed
    /// from a masked column (a nested <c>FOR JSON</c>, a constructor, <c>json_query</c>), which the server writes into the text as its
    /// mask without quotes, <c>{"kids":xxxx}</c> (observed: <c>Json_computed_from_a_masked_column_is_its_mask_and_not_json</c>).
    /// No reader can parse it, so the procedure's results can't be listed. Null when every value is JSON or masked as a string,
    /// which the text holds as a string. A json column's own mask is JSON (<c>{"masked":true}</c>), but a value is not told apart
    /// from one computed from it, so it is read the same, which claims less.
    /// </summary>
    private static UnknownShape? MaskedJson(Row row, SourceLocation at) =>
        row.Columns.Select(c => c.Value).FirstOrDefault(v => v is { IsMasked: true, IsLiveJson: true }) is { MaskedBy: { } by }
            ? new UnknownShape(
                IrUnknownReason.JsonShapeUnknown,
                $"This FOR JSON nests JSON computed from the masked column {by}, which SQL Server writes as its mask without quotes for a caller without UNMASK, so its text is not JSON and the procedure's result sets cannot be listed. Declare \"unmask\": true in the config file's session if the callers hold UNMASK.",
                at)
            : null;

    /// <summary>
    /// What a call of a scalar function gives, masked when the function's body reads a masked column (<see cref="Summaries.ReadsMasked"/>).
    /// The server masks such a call's result whatever the body does with the value, even through a variable and with inlining off
    /// (observed: <c>A_scalar_function_that_reads_a_masked_column_returns_its_mask</c>).
    /// </summary>
    private Value MaskedCall(Value value, Function function, TSqlFragment call) =>
        summaries.MasksApply && summaries.ReadsMasked(function) is { } by ? value.Masked(by, At(call)) : value;
}
