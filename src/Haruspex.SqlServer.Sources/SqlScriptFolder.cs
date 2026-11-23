using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Reads a folder of <c>.sql</c> scripts, such as a SQL Server database project's, into a
/// <see cref="DatabaseModel"/>. It parses with ScriptDom, Microsoft's T-SQL parser, and needs neither DacFx nor a
/// database.
/// </summary>
/// <remarks>
/// <para>
/// Tables are read from <c>CREATE TABLE</c>, and procedures as their script leaves them, its statements played in batch
/// order: a stub created and then altered is its <c>ALTER</c>. A statement in another script that changes either is not
/// applied, because which state wins depends on the order the scripts run, which a folder does not say. Instead it is
/// recorded as a <see cref="Redefinition"/>: on a table for <c>ALTER TABLE</c> (other than adding or checking constraints,
/// which can only tighten it), <c>DROP TABLE</c> or a <c>CREATE TABLE</c> nested in another statement, wherever it is, under
/// <c>IF</c> and in a body included (<c>WeakeningStatements</c>), and on a procedure for <c>ALTER PROCEDURE</c> or
/// <c>DROP PROCEDURE</c> in another script than its definition.
/// </para>
/// <para>
/// Alias and CLR types are read from <c>CREATE TYPE</c>, and CHECK constraints from <c>CREATE TABLE</c>. A check that
/// <c>ALTER TABLE … NOCHECK CONSTRAINT</c> may name (or <c>ALL</c>), or of a table a bulk load without <c>CHECK_CONSTRAINTS</c>
/// writes to, anywhere in a script, procedure body included, is recorded as possibly disabled, unless <c>WITH CHECK CHECK CONSTRAINT</c> validates it again later in the
/// same script (see <c>ConstraintTrust</c>).
/// </para>
/// <para>
/// Functions and views are read with the same rule as procedures (a view that two scripts define records a
/// <see cref="Redefinition"/>), synonyms from <c>CREATE SYNONYM</c> and <c>DROP SYNONYM</c> in the same order (a synonym two
/// scripts create is reported), and DML triggers from every definition of each. Every
/// other statement is passed over (<c>ScriptReader</c> says why).
/// </para>
/// <para>
/// A read is three steps, each in a type of its own: which scripts there are and in which order, each parsed
/// (<c>ScriptFile</c>, each batch with the <c>QUOTED_IDENTIFIER</c> in force there, <c>ScriptParser</c>); what each script declares, applying only its own order (<c>ScriptReader</c>, into
/// <c>SchemaDeclarations</c>);
/// and the passes that settle what is in effect once every script is read (<c>DefinitionsInEffect</c>,
/// <c>ConstraintTrust</c>). The guide's chapter <i>The inputs and the model</i> explains them
/// (<c>docs/internals/02-the-inputs-and-the-model.md</c>).
/// </para>
/// </remarks>
public static class SqlScriptFolder
{
    /// <summary>Reads every <c>*.sql</c> file under <paramref name="directory"/>, including subfolders.</summary>
    /// <remarks>
    /// The folder is listed as a project's build lists its default items, <c>**/*.sql</c>, by the walk the project reader uses
    /// (<c>Projects.Glob</c>): the extension matched ignoring case on every platform (<c>Load.SQL</c> is a script on Linux as on
    /// Windows), hidden folders included, and each folder entered once by where it really is, so a link back up the tree reads each
    /// script once. A folder that can't be entered, and a script that can't be read, is an error naming it (HAR1020), not a fault.
    /// Files are read in order of their relative path, so the outcome does not depend on the file system. Each is
    /// UTF-8 unless a byte order mark says otherwise, and is parsed with the SQL Server 2025 grammar.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public static SchemaReadResult Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        var root = Projects.DiskPath.Locate(Path.GetFullPath(directory), directory, out _) ?? Path.GetFullPath(directory);
        var listed = Projects.Glob.Of(root, "**/*.sql").Files(out var unexpanded, out var unreadable);
        var declarations = new SchemaDeclarations();
        foreach (var folder in unreadable)
            declarations.Diagnostics.Add(SourceCodes.UnreadableScript.At(new FileLocation(Shown(root, folder)), $"{Shown(root, folder)} can't be entered, so no script in it is read."));
        if (listed is null)
            declarations.Diagnostics.Add(SourceCodes.UnreadableScript.At(new FileLocation(directory), $"The folder can't be listed, so no script in it is read: {unexpanded}."));

        ReadInto(declarations, ScriptFile.InOrder(root, (listed ?? []).Select(path => Path.GetRelativePath(root, path)), listed: true));
        return new SchemaReadResult(declarations.ToModel(), [.. declarations.Diagnostics]);
    }

    /// <summary>A folder under the one read, as a person would name it from there.</summary>
    private static string Shown(string root, string folder) => Path.GetRelativePath(root, folder).Replace('\\', '/') + "/";

    /// <summary>
    /// Reads the <c>.sql</c> files named, relative to <paramref name="directory"/>: a chosen set of a folder's scripts, such as
    /// the ones a project file builds.
    /// </summary>
    /// <remarks>As <see cref="Read(string)"/> does, it reads them in order of their relative path.</remarks>
    /// <param name="directory">The project's folder, which positions in the model are relative to.</param>
    /// <param name="files">The scripts, relative to <paramref name="directory"/>, with either kind of slash.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">A file named does not exist.</exception>
    public static SchemaReadResult Read(string directory, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(files);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        // Three steps: which scripts, in which order; what each declares; and the passes that settle what is in effect.
        var declarations = new SchemaDeclarations();
        ReadInto(declarations, ScriptFile.InOrder(directory, files));
        return new SchemaReadResult(declarations.ToModel(), [.. declarations.Diagnostics]);
    }

    /// <summary>
    /// What each script declares, in the order given, each starting with its own settings: a folder's scripts, or a project's,
    /// whose order and settings the project reader settles (<c>Projects.SqlProjectReader</c>).
    /// </summary>
    internal static void ReadInto(SchemaDeclarations declarations, IEnumerable<ScriptFile> scripts)
    {
        foreach (var script in scripts)
        {
            if (script.Parse(declarations.Diagnostics) is { } parsed)
                new ScriptReader(script.Relative, declarations, script.Settings).Read(parsed);
        }
    }
}
