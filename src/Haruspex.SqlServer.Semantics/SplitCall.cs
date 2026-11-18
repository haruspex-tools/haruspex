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
/// <c>value</c>, then <c>ordinal</c> with the literal 1; null when the third argument is anything but the literal 0, 1 or NULL
/// (see <see cref="ColumnsOf"/>).
/// </param>
public sealed record SplitCall(GlobalFunctionTableReference Syntax, QueryScope Arguments, EquatableArray<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? RowsetFunctions.StringSplit.Name, Columns)
{
    /// <summary>
    /// The columns a call gives by its third argument, the ordinal flag (observed:
    /// String_split_gives_value_of_its_strings_type_varying_never_null_and_ordinal_only_when_asked): <c>ordinal</c> with the
    /// literal 1, none with 0 or NULL or without the argument, and null for anything else. SQL Server reads any constant integer
    /// there, <c>CAST(1 AS bit)</c> and <c>1 - 0</c> included, and refuses a variable or column (8748), another value (4199), and
    /// a string, a decimal or a float (8116); only the literals are read here, which leaves the rest unknown.
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
