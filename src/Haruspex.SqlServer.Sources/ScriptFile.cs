using System.Text;
using Haruspex.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Sources;

/// <summary>One script of a folder: where it is on disk, and its path relative to the folder, which every location in it names.</summary>
/// <param name="Path">The file's path.</param>
/// <param name="Relative">Its path relative to the folder, with <c>/</c> separators.</param>
/// <param name="Settings">The settings it starts with: a folder's scripts start with the defaults, a project's with its own.</param>
internal sealed record ScriptFile(string Path, string Relative, ScriptSettings Settings)
{
    /// <summary>A script of a folder, which starts with the default settings.</summary>
    public ScriptFile(string path, string relative)
        : this(path, relative, ScriptSettings.Default)
    {
    }

    /// <summary>
    /// The scripts named, in ordinal order of their relative paths, each once: the order they are read in, so that which of
    /// two definitions of one name is reported doesn't depend on the order they were listed in.
    /// </summary>
    /// <param name="directory">The folder the paths are relative to.</param>
    /// <param name="files">The scripts, relative to <paramref name="directory"/>, with either kind of slash.</param>
    /// <param name="listed">
    /// Whether the files are the folder's own listing, which holds only what is there: one that can't be read then is reported when
    /// it is parsed, as a link to nothing is, rather than refused here.
    /// </param>
    /// <exception cref="FileNotFoundException">A file named does not exist, and the files are not the folder's listing.</exception>
    public static IReadOnlyList<ScriptFile> InOrder(string directory, IEnumerable<string> files, bool listed = false)
    {
        var scripts = files
            .Select(file => file.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(relative => new ScriptFile(System.IO.Path.Combine(directory, relative), relative))
            .ToList();
        if (!listed && scripts.FirstOrDefault(f => !File.Exists(f.Path)) is { } missing)
            throw new FileNotFoundException($"File not found: {missing.Relative}", missing.Path);
        return scripts;
    }

    /// <summary>
    /// Why a file can't be read, in words that are the same on every platform (the exception's own message names the full path,
    /// and in the platform's words).
    /// </summary>
    public static string Why(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "it isn't there, as a link to nothing isn't",
        UnauthorizedAccessException => "the user running haruspex may not open it",
        _ => "it can't be opened, as when another program holds it",
    };

    /// <summary>
    /// The script as ScriptDom parses it with the SQL Server 2025 grammar, from its text as the server would receive it from
    /// sqlcmd, each batch with the <c>QUOTED_IDENTIFIER</c> in force there (<see cref="ScriptParser"/>); null when it doesn't parse, with each error added to <paramref name="diagnostics"/>, or can't be read (a link to
    /// nothing, a file the user may not open, one another process holds), which is an error of the input, not of haruspex.
    /// </summary>
    public TSqlScript? Parse(ICollection<Diagnostic> diagnostics)
    {
        // UTF-8 unless a byte order mark says otherwise, which covers the UTF-16 files SSMS writes.
        string text;
        try
        {
            using var file = new StreamReader(Path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = file.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(SourceCodes.UnreadableScript.At(new SourceLocation(Relative, 1, 1), $"{Relative} can't be read, so nothing in it is: {Why(e)}."));
            return null;
        }

        // What the server would receive from sqlcmd, which an install script written for it needs, parsed batch by batch with
        // QUOTED_IDENTIFIER as the script starts with it and as each SET in it leaves it, which decides whether "x" is a name.
        return ScriptParser.Parse(SqlcmdVariables.Substitute(text), Relative, Settings.QuotedIdentifier, diagnostics);
    }
}
