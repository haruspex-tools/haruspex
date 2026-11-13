using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A <c>GENERATE_SERIES</c> call in FROM, <c>GENERATE_SERIES(1, t.n) AS s</c>: one column, <c>value</c>.</summary>
/// <param name="Syntax">The call.</param>
/// <param name="Arguments">The scope its arguments are read in.</param>
public sealed record SeriesCall(GlobalFunctionTableReference Syntax, QueryScope Arguments)
    : RelationSymbol(Syntax?.Alias?.Value ?? RowsetFunctions.GenerateSeries.Name, ["value"]);
