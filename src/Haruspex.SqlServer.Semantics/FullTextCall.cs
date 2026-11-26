using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// A <c>CONTAINSTABLE</c> or <c>FREETEXTTABLE</c> call in FROM, <c>CONTAINSTABLE(shop.products, description, @q) AS ft</c>: two
/// columns, <c>KEY</c> and <c>RANK</c>, whatever the columns searched (observed:
/// <c>A_full_text_search_returns_key_and_rank_whatever_its_columns_language_and_top_n</c>). Without an alias it is known by its
/// table's own name (<c>note.[KEY]</c>; observed: <c>A_full_text_search_without_an_alias_is_known_by_its_tables_name</c>).
/// </summary>
/// <param name="Syntax">The call.</param>
/// <param name="Searched">The table it searches, bound as a name in FROM is: <see cref="TableTarget"/>, or why not.</param>
public sealed record FullTextCall(FullTextTableReference Syntax, SourceTarget Searched)
    : RelationSymbol(Syntax?.Alias?.Value ?? Syntax?.TableName.BaseIdentifier.Value ?? "", ["KEY", "RANK"])
{
    /// <summary>The function's name as written in messages: <c>CONTAINSTABLE</c> or <c>FREETEXTTABLE</c>.</summary>
    public string Function => Syntax.FullTextFunctionType == FullTextFunctionType.Contains ? "CONTAINSTABLE" : "FREETEXTTABLE";
}
