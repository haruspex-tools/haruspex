using System.Text;
using System.Text.RegularExpressions;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads a query file of the application: one batch, as the application sends it through <c>sp_executesql</c>, and its
/// parameters' declaration comments (<c>-- @param @order_id int</c>). README.md in this folder ("Query files") says what a file
/// may hold; <c>docs/decisions/0059-a-query-file-s-parameters-are-typed-from-their-uses.md</c> why a declaration is a comment.
/// </summary>
public static partial class QueryFileReader
{
    /// <summary>
    /// The query file at <paramref name="path"/>, or every reason it can't be one: SQL that doesn't parse, a file that isn't one
    /// batch, a declaration that can't be taken.
    /// </summary>
    /// <param name="path">The file on disk.</param>
    /// <param name="relative">Its path relative to the folder of the config file it belongs to, with <c>/</c> between folders.</param>
    public static Outcome<QueryFile> Read(string path, string relative)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(relative);

        // UTF-8 unless a byte order mark says otherwise, as a script is read. The text is sent as it is but for its line endings,
        // which are LF so that the IR, which holds it, is the same on every machine; it is parsed so too, so that what is typed
        // is what runs. A string literal spanning lines is the one thing that sees the difference.
        string text;
        using (var file = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = file.ReadToEnd().ReplaceLineEndings("\n");
        return Parse(text, relative);
    }

    /// <summary>The query file whose text is <paramref name="text"/>, read as <see cref="Read"/> reads a file's.</summary>
    /// <param name="text">The text, with LF line endings.</param>
    /// <param name="relative">Its path, which every location in it names.</param>
    public static Outcome<QueryFile> Parse(string text, string relative)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(relative);

        // QUOTED_IDENTIFIER on, as every client library sets it, so "x" is a name, as it is when the application runs it.
        using var reader = new StringReader(text);
        if (new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out var errors) is not TSqlScript script || errors.Count > 0)
            return new Failed<QueryFile>([.. errors.Select(e => new Diagnostic(SourceCodes.ParseError, e.Message, new SourceLocation(relative, e.Line, e.Column)))]);

        if (script.Batches is not [var batch])
        {
            var why = script.Batches.Count == 0
                ? "it holds no statement, so it runs nothing"
                : $"GO splits it into {script.Batches.Count} batches; GO is no part of SQL, but a client's separator, and a query file is run as one batch";
            return new Failed<QueryFile>([SourceCodes.QueryNotOneBatch.At(new SourceLocation(relative, 1, 1), $"{relative} isn't one batch: {why}.")]);
        }

        var statements = new StatementList { ScriptTokenStream = batch.ScriptTokenStream, FirstTokenIndex = batch.FirstTokenIndex, LastTokenIndex = batch.LastTokenIndex };
        foreach (var statement in batch.Statements)
            statements.Statements.Add(statement);

        var problems = new List<Diagnostic>();
        var declared = Declarations(script, relative, statements, problems);
        if (problems.Count > 0)
            return new Failed<QueryFile>([.. problems]);

        var end = SyntaxReader.Span(relative, batch);
        var span = new SourceSpan(relative, 1, 1, end.EndLine, end.EndColumn);
        return new Succeeded<QueryFile>(new QueryFile(relative, span, text, statements, [.. declared]));
    }

    /// <summary>
    /// The parameters the file's declaration comments give a type, in the order written: each line comment that starts
    /// <c>@param</c> is one, and must read as a name and a type that the file uses as a parameter, once.
    /// </summary>
    private static List<Parameter> Declarations(TSqlScript script, string file, StatementList statements, List<Diagnostic> problems)
    {
        var parameters = new List<Parameter>();
        var used = BatchVariables.Undeclared(statements).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = BatchVariables.Declared(statements);
        foreach (var token in script.ScriptTokenStream.Where(t => t.TokenType == TSqlTokenType.SingleLineComment))
        {
            var comment = token.Text[2..].Trim();
            if (!Keyword().IsMatch(comment))
                continue;

            var at = new SourceLocation(file, token.Line, token.Column);
            void Refuse(string why) => problems.Add(SourceCodes.BadParameterDeclaration.At(at, $"-- {comment}: {why}"));
            if (Declaration().Match(comment) is not { Success: true } match)
            {
                Refuse("a declaration is written -- @param @name type, such as -- @param @order_id int.");
                continue;
            }

            var name = match.Groups["name"].Value;
            if (Typed(name, match.Groups["type"].Value) is not { } type)
            {
                Refuse($"\"{match.Groups["type"].Value}\" doesn't read as a type of {name}; write it as DECLARE would.");
                continue;
            }

            if (parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                Refuse($"{name} is declared already; declare a parameter once.");
            else if (declared.Contains(name))
                Refuse($"the file declares {name} itself, with DECLARE, so it is a variable of the batch, not a parameter.");
            else if (!used.Contains(name))
                Refuse($"the file doesn't use {name}, so it isn't a parameter; check its spelling.");
            else
                parameters.Add(new Parameter(name, type, IsOutput: false, Default: null, at));
        }

        return parameters;
    }

    /// <summary>
    /// The type <paramref name="type"/> names for the variable <paramref name="name"/>, read as a <c>DECLARE</c> of it reads, so that
    /// a declaration is written in SQL's own words; null when that doesn't read, or declares anything more.
    /// </summary>
    private static SqlType? Typed(string name, string type)
    {
        if (name.StartsWith("@@", StringComparison.Ordinal))
            return null;
        using var reader = new StringReader($"DECLARE {name} {type};");
        var parsed = new TSql170Parser(initialQuotedIdentifiers: true).Parse(reader, out var errors) as TSqlScript;
        return errors.Count == 0
            && parsed?.Batches is [{ Statements: [DeclareVariableStatement { Declarations: [{ Value: null, DataType: { } dataType } element] }] }]
            && string.Equals(element.VariableName.Value, name, StringComparison.Ordinal)
                ? SyntaxReader.Type(dataType)
                : null;
    }

    /// <summary>A comment that is a declaration, or meant as one: <c>@param</c> as a word of its own at its start.</summary>
    [GeneratedRegex(@"^@param(\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Keyword();

    /// <summary>A declaration: <c>@param</c>, a variable's name, and its type, which runs to the end of the line.</summary>
    [GeneratedRegex(@"^@param\s+(?<name>@\S+)\s+(?<type>\S.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();
}
