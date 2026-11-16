using Haruspex.Collections;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A query file of the application: one batch of SQL that the application runs with its parameters, through
/// <c>sp_executesql</c>, rather than a procedure it calls by name. It isn't part of the database's schema, so a model doesn't
/// hold it: it is read against one.
/// </summary>
/// <remarks>
/// Its parameters are the variables its text uses without declaring, as <c>sp_executesql</c> takes them. A declaration comment
/// in the file (<c>-- @param @order_id int</c>) gives one its type; any other is typed from its uses, by inference.
/// </remarks>
/// <param name="Path">
/// Its path relative to the folder of the config file it belongs to, with <c>/</c> between folders: its identity, and the file
/// every location in it names.
/// </param>
/// <param name="Span">The whole of its text.</param>
/// <param name="Text">What it runs: the file's text, with LF line endings.</param>
/// <param name="Statements">Its statements, the one batch, as parsed from <paramref name="Text"/>.</param>
/// <param name="Declared">The parameters its declaration comments give a type, in the order written, each at its comment.</param>
public sealed record QueryFile(string Path, SourceSpan Span, string Text, StatementList Statements, EquatableArray<Parameter> Declared)
{
    /// <summary>The file's path.</summary>
    public override string ToString() => Path;
}
