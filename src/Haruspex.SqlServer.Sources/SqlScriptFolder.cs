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
        // The model first: settling it reports what only every script together shows (SchemaDeclarations.SettleSynonyms).
        var model = declarations.ToModel();
        return new SchemaReadResult(model, [.. declarations.Diagnostics, .. CaseVariants.In(model)]);
    }

    /// <summary>
    /// Reads every <c>*.sql</c> file under each of <paramref name="folders"/> as one database: a database kept as folders side by
    /// side (<c>tables/</c>, <c>procedures/</c>), beside folders that aren't the database's. Each folder is listed as
    /// <see cref="Read(string)"/> lists one, and positions in the model are relative to <paramref name="directory"/>, so that each
    /// says which folder its script is in (<c>tables/orders.sql</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// No script is read twice, as no script of one folder is: two entries that are the same folder (in any spelling this file
    /// system takes for the same, its case included where it ignores case), one inside the other, or two that reach one folder
    /// through a link, is an error naming both (HAR1023), and nothing is read. Read anyway, each of those scripts would define its
    /// objects twice. Each entry is compared by where it really is (<see cref="Projects.DiskPath.Real"/>), and each folder a walk
    /// enters by where its files really are.
    /// </para>
    /// <para>
    /// Two spellings of one folder that only the file system knows are one, a drive that is another folder under a second name or
    /// a folder that differs only in case on a file system that ignores case where this platform's usually doesn't (a Windows
    /// drive under WSL), aren't seen as one: their scripts are then read twice and define their objects twice (HAR1002), never
    /// typed wrongly. <see cref="Projects.DiskPath.Real"/> gives the same limit.
    /// </para>
    /// <para>The scripts are read in order of their path relative to <paramref name="directory"/>, so the order of the list doesn't matter.</para>
    /// </remarks>
    /// <param name="directory">The folder the list is written in (the config file's), which positions are relative to.</param>
    /// <param name="folders">The folders, full or relative to <paramref name="directory"/>, as written.</param>
    /// <exception cref="DirectoryNotFoundException">A folder listed does not exist.</exception>
    public static SchemaReadResult ReadFolders(string directory, IReadOnlyList<string> folders)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(folders);
        var declarations = new SchemaDeclarations();
        var entries = new List<(string Written, string Root, string Real)>();
        foreach (var written in folders)
        {
            var full = Path.GetFullPath(Path.Combine(directory, written));
            if (!Directory.Exists(full))
                throw new DirectoryNotFoundException($"Directory not found: {written}");
            var root = Projects.DiskPath.Locate(full, written, out _) ?? full;
            entries.Add((written, root, Projects.DiskPath.Real(root) ?? root));
        }

        foreach (var overlap in Overlaps(entries))
            declarations.Diagnostics.Add(SourceCodes.FoldersOverlap.At(null, overlap));
        if (declarations.Diagnostics.Count > 0)
            return new SchemaReadResult(declarations.ToModel(), [.. declarations.Diagnostics]);

        var files = new List<string>();
        var reached = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (written, root, _) in entries)
        {
            var listed = Projects.Glob.Of(root, "**/*.sql").Files(out var unexpanded, out var unreadable);
            foreach (var folder in unreadable)
                declarations.Diagnostics.Add(SourceCodes.UnreadableScript.At(new FileLocation(Shown(directory, folder)), $"{Shown(directory, folder)} can't be entered, so no script in it is read."));
            if (listed is null)
                declarations.Diagnostics.Add(SourceCodes.UnreadableScript.At(new FileLocation(written), $"The folder can't be listed, so no script in it is read: {unexpanded}."));
            foreach (var real in (listed ?? []).Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.Ordinal).Select(f => Projects.DiskPath.Real(f) ?? f))
            {
                if (!reached.TryAdd(real, written) && reached[real] != written)
                    declarations.Diagnostics.Add(SourceCodes.FoldersOverlap.At(null, $"{reached[real]} and {written} both reach {Shown(directory, real)} (through a link), so its scripts would be read twice. List each folder once, or remove the link."));
            }

            files.AddRange(listed ?? []);
        }

        if (declarations.Diagnostics.Any(d => d.Code == SourceCodes.FoldersOverlap))
            return new SchemaReadResult(new SchemaDeclarations().ToModel(), [.. declarations.Diagnostics.DistinctBy(d => d.Message)]);

        ReadInto(declarations, ScriptFile.InOrder(directory, files.Select(file => Path.GetRelativePath(directory, file)), listed: true));
        var model = declarations.ToModel();
        return new SchemaReadResult(model, [.. declarations.Diagnostics, .. CaseVariants.In(model)]);
    }

    /// <summary>Each pair of entries that is one folder, or one inside the other, said once, in the order listed.</summary>
    private static IEnumerable<string> Overlaps(List<(string Written, string Root, string Real)> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            for (var j = i + 1; j < entries.Count; j++)
            {
                var (a, b) = (entries[i], entries[j]);
                if (a.Real == b.Real)
                    yield return $"{a.Written} and {b.Written} are the same folder, so its scripts would be read twice. List it once.";
                else if (Inside(b.Real, a.Real))
                    yield return $"{b.Written} is inside {a.Written}, so its scripts would be read twice. List {a.Written} alone, or list its folders but not it.";
                else if (Inside(a.Real, b.Real))
                    yield return $"{a.Written} is inside {b.Written}, so its scripts would be read twice. List {b.Written} alone, or list its folders but not it.";
            }
        }
    }

    /// <summary>Whether the folder <paramref name="inner"/> is below <paramref name="outer"/>, both real paths.</summary>
    private static bool Inside(string inner, string outer) =>
        inner.StartsWith(Path.TrimEndingDirectorySeparator(outer) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

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
        // The model first: settling it reports what only every script together shows (SchemaDeclarations.SettleSynonyms).
        var model = declarations.ToModel();
        return new SchemaReadResult(model, [.. declarations.Diagnostics, .. CaseVariants.In(model)]);
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
