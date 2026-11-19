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
    /// <summary>
    /// The names its sets comment gives its result sets, in order (<c>-- @sets order, lines</c>), as written; null when it has
    /// none. Whether each can name a set, and whether there is one for every set, is the config layer's to check, as it checks a
    /// procedure's <c>sets</c> (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>).
    /// </summary>
    public EquatableArray<string>? Sets { get; init; }

    /// <summary>Where its sets comment is, when it has one.</summary>
    public SourceLocation? SetsAt { get; init; }

    /// <summary>
    /// The module its module comment puts it in (<c>-- @module reports</c>), as written, for a config file that splits its output
    /// into modules; null when it has none. Whether the config file has such a module is the config layer's to check.
    /// </summary>
    public string? Module { get; init; }

    /// <summary>Where its module comment is, when it has one.</summary>
    public SourceLocation? ModuleAt { get; init; }

    /// <summary>The file's path.</summary>
    public override string ToString() => Path;
}
