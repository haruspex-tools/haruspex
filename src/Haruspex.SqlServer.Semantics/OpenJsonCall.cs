using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>An <c>OPENJSON</c> call in FROM, <c>OPENJSON(t.doc) WITH (…) AS j</c>.</summary>
/// <param name="Syntax">The call.</param>
/// <param name="Arguments">The scope its arguments are read in.</param>
/// <param name="Columns"><c>key</c>, <c>value</c> and <c>type</c> without <c>WITH</c>, its <c>WITH</c> columns with it; null when two have one name.</param>
public sealed record OpenJsonCall(OpenJsonTableReference Syntax, QueryScope Arguments, IReadOnlyList<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? "OPENJSON", Columns);
