using Haruspex.Collections;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// An <c>OPENXML</c> call in FROM, <c>OPENXML(@handle, '/orders/order', 1) WITH (…) AS x</c>: a row for each node its pattern
/// finds in the document <c>sp_xml_preparedocument</c> parsed.
/// </summary>
/// <param name="Syntax">The call.</param>
/// <param name="Arguments">The scope its arguments are read in.</param>
/// <param name="Table">With <c>WITH</c> a table's name, the table it names, when it is one of the project's named with its schema.</param>
/// <param name="Columns">
/// Its <c>WITH</c> columns; with a table's name, that table's; without <c>WITH</c>, the edge table's (<see cref="EdgeColumns"/>);
/// null with a table's name that isn't <see cref="Table"/>, and when two have one name.
/// </param>
public sealed record OpenXmlCall(OpenXmlTableReference Syntax, QueryScope Arguments, Table? Table, EquatableArray<string>? Columns)
    : RelationSymbol(Syntax?.Alias?.Value ?? "OPENXML", Columns)
{
    /// <summary>The columns of the edge table <c>OPENXML</c> gives without <c>WITH</c>, in order (observed: Openxml_without_with_is_the_edge_table).</summary>
    public static EquatableArray<string> EdgeColumns { get; } = ["id", "parentid", "nodetype", "localname", "prefix", "namespaceuri", "datatype", "prev", "text"];

    /// <summary>
    /// The table <c>WITH</c> names, when it is the project's and named with its schema; null otherwise. One named without a
    /// schema is found by the caller's rules, which aren't read here, so it is left unknown.
    /// </summary>
    public static Table? TableOf(OpenXmlTableReference call, DatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(model);
        return call.TableName is { SchemaIdentifier: not null, DatabaseIdentifier: null, ServerIdentifier: null } name ? model.FindTable(SyntaxReader.Name(name)) : null;
    }

    /// <summary>The columns a call gives, as <see cref="RelationSymbol.Columns"/> says.</summary>
    public static EquatableArray<string>? ColumnsOf(OpenXmlTableReference call, Table? table)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call.TableName is not null)
            return table is null ? null : [.. table.Columns.Select(c => c.Name)];
        if (call.SchemaDeclarationItems.Count == 0)
            return EdgeColumns;
        List<string> names = [.. call.SchemaDeclarationItems.Select(i => i.ColumnDefinition.ColumnIdentifier.Value)];
        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count ? [.. names] : null;
    }
}
