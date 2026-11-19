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
/// which can only tighten it) or <c>DROP TABLE</c>, wherever it is, and on a procedure for <c>ALTER PROCEDURE</c> or
/// <c>DROP PROCEDURE</c> in another script than its definition.
/// </para>
/// <para>
/// Alias and CLR types are read from <c>CREATE TYPE</c>, and CHECK constraints from <c>CREATE TABLE</c>. A check that
/// <c>ALTER TABLE … NOCHECK CONSTRAINT</c> may name (or <c>ALL</c>), anywhere in a script, procedure body included,
/// is recorded as possibly disabled, unless <c>WITH CHECK CHECK CONSTRAINT</c> validates it again later in the
/// same script (see <c>ConstraintTrust</c>).
/// </para>
/// <para>
/// Functions and views are read with the same rule as procedures (a view that two scripts define records a
/// <see cref="Redefinition"/>), synonyms from <c>CREATE SYNONYM</c>, and DML triggers from every definition of each. Every
/// other statement is passed over (<c>ScriptReader</c> says why).
/// </para>
/// <para>
/// A read is three steps, each in a type of its own: which scripts there are and in which order, each parsed
/// (<c>ScriptFile</c>); what each script declares, applying only its own order (<c>ScriptReader</c>, into
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
    /// Files are read in order of their relative path, so the outcome does not depend on the file system. Each is
    /// UTF-8 unless a byte order mark says otherwise, and is parsed with the SQL Server 2025 grammar.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    public static SchemaReadResult Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        return Read(directory, Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path)));
    }

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
