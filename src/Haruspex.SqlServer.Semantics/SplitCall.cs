using Haruspex.Collections;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A <c>STRING_SPLIT</c> call in FROM, <c>STRING_SPLIT(t.tags, N',', 1) AS v</c>: <c>value</c>, and <c>ordinal</c> when its third
/// argument is the literal 1.
/// </summary>
/// <param name="Syntax">The call.</param>
/// <param name="Arguments">The scope its arguments are read in.</param>
/// <param name="Columns">
/// <c>value</c>, then <c>ordinal</c> with the literal 1; null when the third argument is anything but the literal 0, 1 or NULL,
/// which SQL Server refuses.
/// </param>
public sealed record SplitCall(GlobalFunctionTableReference Syntax, QueryScope Arguments, EquatableArray<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? RowsetFunctions.StringSplit.Name, Columns)
{
    /// <summary>
    /// The columns a call gives by its third argument, the ordinal flag, which SQL Server reads only as a literal (observed:
    /// String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked): <c>ordinal</c> with 1, none
    /// with 0 or NULL or without the argument, and null for anything else, which it refuses (8748, 4199).
    /// </summary>
    public static EquatableArray<string>? ColumnsOf(GlobalFunctionTableReference call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Parameters.Count < 3 ? ["value"] : call.Parameters[2] switch
        {
            IntegerLiteral { Value: "1" } => ["value", "ordinal"],
            IntegerLiteral { Value: "0" } or NullLiteral => ["value"],
            _ => null,
        };
    }
}
