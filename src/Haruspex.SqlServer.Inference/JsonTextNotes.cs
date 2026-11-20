using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Finds JSON built as text by its construction, wherever it is written, in every module of the project and every query file:
/// the inventory for modernisation the note <see cref="InferenceCodes.JsonBuiltAsText"/> gives (docs/decisions/0085-json-built-as-text-is-a-note-wherever-it-is-written.md). It reads the
/// SQL alone, so it reports what is never read as JSON too, which typing would never see: a column that holds JSON assembled
/// as text and that no caller reads with <c>json_query</c> is, rightly, a string.
/// </summary>
/// <remarks>
/// Two constructions are reported, each once, at the outermost expression. One is a concatenation (<c>+</c> or <c>concat</c>)
/// whose text is a JSON array, each part that is not a literal standing for a value or for text inside a string, and that shows
/// it is JSON: a comma, a quotation mark or a brace in its literals, a <c>string_agg</c> or <c>concat_ws</c>, or a part that
/// builds JSON. That leaves out T-SQL's own brackets, <c>'[' + name + ']'</c> quoting an identifier and text such as
/// <c>'[' + n + ' KEYS]'</c>, which a reader of the corpora meets often. The other is <c>concat_ws</c> with a comma between values
/// one of which is a literal JSON string, <c>'"retail"'</c>, whose brackets may be elsewhere.
/// </remarks>
internal static class JsonTextNotes
{
    /// <summary>A note for each place JSON is built as text, in the order of their files and positions.</summary>
    public static IEnumerable<Diagnostic> In(DatabaseModel model, IReadOnlyList<QueryFile> queries)
    {
        var bodies = new List<(SourceSpan Span, TSqlFragment? Body)>();
        bodies.AddRange(model.Procedures.Select(p => (p.Span, (TSqlFragment?)Statements(p.Body))));
        bodies.AddRange(model.Triggers.Select(t => (t.Span, (TSqlFragment?)Statements(t.Body))));
        bodies.AddRange(model.Views.Select(v => (v.Span, (TSqlFragment?)v.Query)));
        bodies.AddRange(model.Functions.Select(f => (f.Span, f switch
        {
            ScalarFunction scalar => (TSqlFragment?)Statements(scalar.Body),
            TableFunction table => Statements(table.Body),
            InlineFunction inline => inline.Query,
        })));
        bodies.AddRange(queries.Select(q => (q.Span, (TSqlFragment?)q.Statements)));

        var notes = new List<(SourceLocation At, Diagnostic Note)>();
        foreach (var (span, body) in bodies)
        {
            if (body is null)
                continue;
            var finder = new Finder();
            body.Accept(finder);
            notes.AddRange(finder.Found.Select(f => (span.At(f.Expression), InferenceCodes.JsonBuiltAsText.At(span.At(f.Expression), Message(f.Expression, f.Aggregated)))));
        }

        return notes
            .OrderBy(n => n.At.File, StringComparer.Ordinal).ThenBy(n => n.At.Line).ThenBy(n => n.At.Column)
            .Select(n => n.Note);
    }

    private static StatementList? Statements(ModuleBody body) => body is SqlBody { Statements: var statements } ? statements : null;

    private static string Message(ScalarExpression expression, bool aggregated) => aggregated
        ? $"{FragmentText.OneLine(expression)} builds a JSON array as text; build it with json_arrayagg(…) (SQL Server 2025, Azure SQL), which escapes each value and which haruspex types exactly. Like string_agg, it is NULL over no rows."
        : $"{FragmentText.OneLine(expression)} builds a JSON array as text; build it with json_array(…) (SQL Server 2022 on), which escapes each value, leaves out a NULL as concat_ws does, and which haruspex types exactly.";

    /// <summary>Visits a body, recording each outermost construction and not looking inside it again.</summary>
    private sealed class Finder : TSqlFragmentVisitor
    {
        public List<(ScalarExpression Expression, bool Aggregated)> Found { get; } = [];

        public override void ExplicitVisit(BinaryExpression node)
        {
            if (!Record(node))
                base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FunctionCall node)
        {
            if (!Record(node))
                base.ExplicitVisit(node);
        }

        private bool Record(ScalarExpression node)
        {
            if (!Bracketed(node) && !QuotedWs(node))
                return false;
            var aggregates = new AggregateFinder();
            node.Accept(aggregates);
            Found.Add((node, aggregates.Found));
            return true;
        }
    }

    /// <summary>Whether a <c>string_agg</c> is written anywhere in an expression.</summary>
    private sealed class AggregateFinder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(FunctionCall node)
        {
            Found |= node.CallTarget is null && BuiltInFunctions.Named(node.FunctionName.Value, FunctionForm.Aggregate) == AggregateFunctions.StringAgg;
            base.Visit(node);
        }
    }

    /// <summary>
    /// A concatenation whose text is a JSON array, not all literals, that shows it is JSON (<see cref="JsonTextNotes"/>).
    /// </summary>
    private static bool Bracketed(ScalarExpression node)
    {
        if (!IsConcatenation(node))
            return false;
        var parts = new List<ScalarExpression>();
        Flatten(node, parts);
        if (parts is not [StringLiteral first, .., StringLiteral] || !first.Value.TrimStart().StartsWith('[') || parts.All(p => p is StringLiteral) || !LexesAsArray(parts))
            return false;
        var builders = new JsonBuilderFinder();
        node.Accept(builders);
        return builders.Found || parts.OfType<StringLiteral>().Any(l => l.Value.IndexOfAny(['"', ',', '{']) >= 0);
    }

    /// <summary>
    /// Whether the parts' text is one JSON array, each part that is not a literal read as a value, or as text where it is inside
    /// a string: the lexical shape of JSON, not its content.
    /// </summary>
    private static bool LexesAsArray(List<ScalarExpression> parts)
    {
        var open = new Stack<char>();
        var expect = 'v'; // v a value, c a comma or a close, k a key or a close, : a colon, s inside a string, d done
        var key = false;
        foreach (var part in parts)
        {
            if (part is not StringLiteral { Value: var text })
            {
                if (expect is 'v' && open.Count > 0)
                    expect = 'c';
                else if (expect is 'k')
                    (expect, key) = (':', false);
                else if (expect is not 's')
                    return false;
                continue;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (expect == 's')
                {
                    if (c == '\\')
                        i++;
                    else if (c == '"')
                        expect = key ? ':' : 'c';
                    continue;
                }

                if (c is ' ' or '\t' or '\r' or '\n')
                    continue;
                switch (expect)
                {
                    case 'v' when c is '[' or '{':
                        open.Push(c);
                        expect = c == '[' ? 'v' : 'k';
                        break;
                    case 'v' when c == ']' && open.Count > 0 && open.Peek() == '[':
                    case 'k' when c == '}':
                    case 'c' when c is ']' or '}' && open.Count > 0 && open.Peek() == (c == ']' ? '[' : '{'):
                        open.Pop();
                        expect = open.Count == 0 ? 'd' : 'c';
                        break;
                    case 'v' when c == '"':
                        (expect, key) = ('s', false);
                        break;
                    case 'k' when c == '"':
                        (expect, key) = ('s', true);
                        break;
                    case ':' when c == ':':
                        expect = 'v';
                        break;
                    case 'c' when c == ',' && open.Count > 0:
                        expect = open.Peek() == '[' ? 'v' : 'k';
                        break;
                    case 'v' when char.IsAsciiLetterOrDigit(c) || c is '-' or '.':
                        while (i + 1 < text.Length && (char.IsAsciiLetterOrDigit(text[i + 1]) || text[i + 1] is '-' or '+' or '.'))
                            i++;
                        expect = 'c';
                        break;
                    default:
                        return false;
                }
            }
        }

        return expect == 'd';
    }

    /// <summary>Whether a <c>string_agg</c>, <c>concat_ws</c>, a JSON constructor, <c>json_query</c> or <c>FOR JSON</c> is written in an expression.</summary>
    private sealed class JsonBuilderFinder : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is null)
            {
                Found |= (BuiltInFunctions.Named(node.FunctionName.Value, FunctionForm.Aggregate) is { } aggregate
                        && (aggregate == AggregateFunctions.StringAgg || aggregate == AggregateFunctions.JsonArrayAgg || aggregate == AggregateFunctions.JsonObjectAgg))
                    || (BuiltInFunctions.Named(node.FunctionName.Value, FunctionForm.Scalar) is { } scalar
                        && (scalar == ScalarFunctions.ConcatWithSeparator || scalar == ScalarFunctions.JsonObject || scalar == ScalarFunctions.JsonArray || scalar == ScalarFunctions.JsonQuery));
            }

            base.Visit(node);
        }

        public override void Visit(JsonForClause node)
        {
            Found = true;
            base.Visit(node);
        }
    }

    /// <summary><c>concat_ws(',', …)</c> with a literal JSON string among its values, or among a <c>CASE</c>'s results there.</summary>
    private static bool QuotedWs(ScalarExpression node) =>
        node is FunctionCall { CallTarget: null, Parameters: { Count: >= 2 } arguments } call
        && BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.ConcatWithSeparator
        && arguments[0] is StringLiteral { Value: var separator } && separator.Trim() == ","
        && arguments.Skip(1).Any(Quoted);

    private static bool Quoted(ScalarExpression value) => Typer.Strip(value) switch
    {
        StringLiteral literal => literal.Value.Trim() is { Length: >= 2 } text && text[0] == '"' && text[^1] == '"',
        SearchedCaseExpression searched => searched.WhenClauses.Any(w => Quoted(w.ThenExpression)) || (searched.ElseExpression is { } e && Quoted(e)),
        SimpleCaseExpression simple => simple.WhenClauses.Any(w => Quoted(w.ThenExpression)) || (simple.ElseExpression is { } e && Quoted(e)),
        IIfCall iif => Quoted(iif.ThenExpression) || Quoted(iif.ElseExpression),
        _ => false,
    };

    private static bool IsConcatenation(ScalarExpression node) => node switch
    {
        BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } => true,
        FunctionCall { CallTarget: null } call => BuiltInFunctions.Named(call.FunctionName.Value, FunctionForm.Scalar) == ScalarFunctions.Concat,
        _ => false,
    };

    /// <summary>The parts a concatenation joins, through parentheses and nested concatenations.</summary>
    private static void Flatten(ScalarExpression node, List<ScalarExpression> parts)
    {
        var stripped = Typer.Strip(node);
        switch (stripped)
        {
            case BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add } plus:
                Flatten(plus.FirstExpression, parts);
                Flatten(plus.SecondExpression, parts);
                break;
            case FunctionCall call when IsConcatenation(call):
                foreach (var argument in call.Parameters)
                    Flatten(argument, parts);
                break;
            default:
                parts.Add(stripped);
                break;
        }
    }
}
