using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Finds each result set a procedure or a query file returns as <c>FOR XML</c>, for the note
/// <see cref="InferenceCodes.ForXmlResult"/>: the mssql driver joins its rows into one string and never parses it (observed:
/// <c>For_xml_is_joined_into_one_row_and_never_parsed</c>), so the application receives XML it must parse itself, which haruspex
/// doesn't type, where <c>FOR JSON</c> builds the same data and is typed. A note beside the result's unknown, an inventory for
/// modernisation as HAR2025 is. Why: docs/decisions/0133-an-xml-value-is-a-kind-of-its-own-a-branded-string.md.
/// </summary>
/// <remarks>
/// Only a <c>SELECT</c> that is a statement of its own returns a result set; a <c>FOR XML</c> subquery is a value inside another
/// query, and builds text, not a result set.
/// </remarks>
internal static class XmlResultNotes
{
    /// <summary>A note for each <c>FOR XML</c> result set of each procedure and query file, in the order of their files and positions.</summary>
    public static IEnumerable<Diagnostic> In(DatabaseModel model, IReadOnlyList<QueryFile> queries)
    {
        var bodies = model.Procedures.Select(p => (p.Span, Body: p.Body is SqlBody { Statements: var statements } ? (TSqlFragment)statements : null))
            .Concat(queries.Select(q => (q.Span, Body: (TSqlFragment?)q.Statements)));
        var notes = new List<(SourceLocation At, Diagnostic Note)>();
        foreach (var (span, body) in bodies)
        {
            if (body is null)
                continue;
            var finder = new Finder();
            body.Accept(finder);
            notes.AddRange(finder.Found.Select(clause => (span.At(clause), InferenceCodes.ForXmlResult.At(span.At(clause), Message(clause)))));
        }

        return notes
            .OrderBy(n => n.At.File, StringComparer.Ordinal).ThenBy(n => n.At.Line).ThenBy(n => n.At.Column)
            .Select(n => n.Note);
    }

    private static string Message(XmlForClause clause) =>
        $"{FragmentText.OneLine(clause)} returns its rows as XML, which the mssql driver joins into one string it doesn't parse, so the application must parse it itself and haruspex doesn't type it; FOR JSON PATH builds the same data as JSON, which haruspex types exactly.";

    /// <summary>Each <c>SELECT</c> statement's own <c>FOR XML</c>, the last query's of a set operation, where the clause belongs to the whole.</summary>
    private sealed class Finder : TSqlFragmentVisitor
    {
        public List<XmlForClause> Found { get; } = [];

        public override void Visit(SelectStatement node)
        {
            if (node.Into is null && Typer.ForOf(node.QueryExpression) is XmlForClause clause)
                Found.Add(clause);
        }
    }
}
