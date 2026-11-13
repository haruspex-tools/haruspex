using System.Text;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>One script of a folder: where it is on disk, and its path relative to the folder, which every location in it names.</summary>
/// <param name="Path">The file's path.</param>
/// <param name="Relative">Its path relative to the folder, with <c>/</c> separators.</param>
internal sealed record ScriptFile(string Path, string Relative)
{
    /// <summary>
    /// The scripts named, in ordinal order of their relative paths, each once: the order they are read in, so that which of
    /// two definitions of one name is reported doesn't depend on the order they were listed in.
    /// </summary>
    /// <param name="directory">The folder the paths are relative to.</param>
    /// <param name="files">The scripts, relative to <paramref name="directory"/>, with either kind of slash.</param>
    /// <exception cref="FileNotFoundException">A file named does not exist.</exception>
    public static IReadOnlyList<ScriptFile> InOrder(string directory, IEnumerable<string> files)
    {
        var scripts = files
            .Select(file => file.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(relative => new ScriptFile(System.IO.Path.Combine(directory, relative), relative))
            .ToList();
        if (scripts.FirstOrDefault(f => !File.Exists(f.Path)) is { } missing)
            throw new FileNotFoundException($"File not found: {missing.Relative}", missing.Path);
        return scripts;
    }

    /// <summary>
    /// The script as ScriptDom parses it with the SQL Server 2025 grammar, from its text as the server would receive it from
    /// sqlcmd; null when it doesn't parse, with each error added to <paramref name="diagnostics"/>.
    /// </summary>
    public TSqlScript? Parse(ICollection<Diagnostic> diagnostics)
    {
        // UTF-8 unless a byte order mark says otherwise, which covers the UTF-16 files SSMS writes.
        string text;
        using (var file = new StreamReader(Path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = file.ReadToEnd();

        // What the server would receive from sqlcmd, which an install script written for it needs.
        using var reader = new StringReader(SqlcmdVariables.Substitute(text));

        // The SQL Server 2025 grammar. Later the target platform will come from the project file.
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out var errors);

        // A parse error is a position the parser gives, not a fragment: the one place a location is made from a line and column.
        foreach (var error in errors)
            diagnostics.Add(new Diagnostic(SourceCodes.ParseError, error.Message, new SourceLocation(Relative, error.Line, error.Column)));
        return errors.Count == 0 ? fragment as TSqlScript : null;
    }
}
