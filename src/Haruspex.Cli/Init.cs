using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Cli.Output;
using Haruspex.Collections;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// <c>haruspex init</c>: finds the database near the working directory, a project file (<c>.sqlproj</c>), a DACPAC or a folder of
/// <c>.sql</c> files, and writes a starter <c>haruspex.json</c> that reads it and types every procedure it defines, then says
/// what to run next. It never writes over a config file. Its first run is a developer's first minute with the tool, so it
/// guesses only what it can check (the source is there, the schemas hold procedures) and names what it chose, for the
/// developer to narrow.
/// </summary>
internal static class Init
{
    /// <summary>How far below the working directory it looks: a project is usually a folder or two down (<c>database/Sales/Sales.sqlproj</c>).</summary>
    private const int Depth = 4;

    /// <summary>
    /// Packages a build copies beside a project's own, which describe SQL Server's system databases rather than the application's.
    /// </summary>
    private static readonly HashSet<string> SystemPackages = new(StringComparer.OrdinalIgnoreCase) { "master.dacpac", "msdb.dacpac", "model.dacpac", "tempdb.dacpac" };

    /// <summary>
    /// Runs the command: <paramref name="source"/>, if given, rather than what it finds. The file names the config file's schema at
    /// the address the build in <paramref name="terminal"/> names it (<see cref="SchemaAddress"/>).
    /// </summary>
    /// <returns>
    /// The exit code: 0; 2 when it finds a config file, no database or more than one; 1 when it can't write, or when reading the source
    /// finds errors, which it says.
    /// </returns>
    public static int Run(string? source, TextWriter output, TextWriter error, string workingDirectory, Terminal terminal)
    {
        var config = Path.Join(workingDirectory, HaruspexConfig.FileName);
        if (File.Exists(config))
            return Refused(error, CliCodes.ConfigExists.At(new FileLocation(HaruspexConfig.FileName), "a config file is already here, and init doesn't write over one. Edit it, or remove it to start again."));

        var found = source is null ? Find(workingDirectory) : Given(source, workingDirectory);
        if (found is Failed<Chosen> failed)
            return Refused(error, [.. failed.Diagnostics]);
        var picked = ((Succeeded<Chosen>)found).Value;
        var (chosen, leftOut, readAsDatabase, parts, listed) = picked;

        // Relative to the config file's folder, as every path in the file is, even when it climbs out (../db): the file is
        // committed, and an absolute path would hold only on this machine. Only a source on another drive stays absolute.
        List<string> written = listed.Count > 0
            ? [.. listed.Select(f => DiagnosticFormatter.PathAsShown(Path.GetRelativePath(workingDirectory, f)))]
            : [DiagnosticFormatter.PathAsShown(Path.GetRelativePath(workingDirectory, chosen))];
        var shown = EnglishList.Of(written);
        var (schemas, errors) = listed.Count > 0 ? Schemas(workingDirectory, written) : Schemas(chosen);
        var @out = Directory.Exists(Path.Join(workingDirectory, "src")) ? "src/db" : "generated";
        try
        {
            // CreateNew, so a file that appeared since the check above is left as it is.
            using var file = new FileStream(config, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(Text(written, @out, schemas, SchemaAddress.Of(SchemaAddress.ConfigFile, terminal)));
        }
        catch (IOException e) when (File.Exists(config))
        {
            return Refused(error, CliCodes.ConfigExists.At(new FileLocation(HaruspexConfig.FileName), $"a config file appeared while init ran, and it doesn't write over one: {e.Message}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // As for any output that can't be written: the command line was right, so 1, not 2 (ModuleRun.ExitCode).
            error.WriteLine(CliCodes.CannotWrite.At(null, $"cannot write {HaruspexConfig.FileName}: {e.Message}"));
            return HaruspexCli.ExitCodes.Failed;
        }

        // Names from the files found are printed as the text report prints them, control characters escaped.
        var shownSchemas = schemas.Select(Visible.Of).ToList();
        shown = EnglishList.Of([.. written.Select(Visible.Of)]);

        // What reading the source found wrong is said here, since the next generate may stop at something else first (with no
        // schema selected, at HAR6007), and the run fails, as any read with errors does. The file is kept: the source is still the
        // one to read once the errors are corrected, and init never writes over a config file, so it couldn't be run again.
        if (errors.Count > 0)
        {
            foreach (var problem in errors)
                error.WriteLine(problem);
            output.WriteLine($"haruspex: wrote {HaruspexConfig.FileName}, which reads {shown}, but reading it gave {(errors.Count == 1 ? "1 error. Correct it" : $"{errors.Count} errors. Correct them")}, then run haruspex generate.");
            return HaruspexCli.ExitCodes.Failed;
        }

        output.WriteLine(schemas.Count == 0
            ? $"haruspex: wrote {HaruspexConfig.FileName}, which reads {shown}, where no procedure was found, so it selects none yet. Add a schema's \"include\", or \"queries\" for the application's query files, then run haruspex generate."
            : $"haruspex: wrote {HaruspexConfig.FileName}, which reads {shown} and types every procedure in {EnglishList.Of(shownSchemas)}, written to {@out}.");
        if (schemas.Count > 0)
        {
            output.WriteLine("Next, run haruspex generate. Then narrow each schema's \"include\" to the procedures the application calls.");

            // A team's format on save or pre-commit hook meets the generated files first, and check compares them as text.
            output.WriteLine($"Keep {@out} out of your formatter (in .prettierignore, or as \"!{@out}\" after \"**\" in Biome's files.includes): haruspex check fails on a file a formatter restyled.");
        }

        if (leftOut.Count > 0)
        {
            var folders = leftOut.Select(f => Visible.Of(DisplayPath.From(workingDirectory, f))).ToList();
            var queries = folders.Count == 1 ? Quoted(folders[0]) : $"[{string.Join(", ", folders.Select(Quoted))}]";
            output.WriteLine($"The .sql files in {EnglishList.Of(folders)} add nothing to the database, so the source leaves them out. If they are the application's query files, add \"queries\": {queries} to type them.");
        }

        if (readAsDatabase.Count > 0)
        {
            var folders = readAsDatabase.Select(f => Visible.Of(DisplayPath.From(workingDirectory, f))).ToList();
            List<string> holders = listed.Count > 0
                ? [.. listed.Where(l => readAsDatabase.Any(f => Below(f, l))).Select(l => Visible.Of(DisplayPath.From(workingDirectory, l)))]
                : [shown];
            var line = $"The .sql files in {EnglishList.Of(folders)} add nothing to the database, but {EnglishList.Of(holders)} {(holders.Count == 1 ? "holds" : "hold")} them, so they are read as the database's scripts and not typed.";
            if (parts.Count > 0)
            {
                // Advice to bring the database's parts together, not to move one into another: which of them is the database's own
                // folder isn't known.
                var named = parts.Select(p => Visible.Of(DisplayPath.From(workingDirectory, p))).ToList();
                var put = named.Count == 1 ? $"put {named[0]} in a folder of its own" : $"put {EnglishList.Of(named)} together in one folder of their own";
                line += $" It reads {shown} because what adds to the database is in {EnglishList.Of(named)}. If {EnglishList.Of(folders)} {(folders.Count == 1 ? "holds" : "hold")} the application's query files, {put}, remove {HaruspexConfig.FileName} and run haruspex init again.";
            }

            output.WriteLine(line);
        }

        Stray(output, picked, workingDirectory, listed.Count > 0 ? null : shown, listed);
        return HaruspexCli.ExitCodes.Success;
    }

    /// <summary>
    /// Names the query scripts no folder named takes in (<see cref="Chosen.StraysLeftOut"/>, <see cref="Chosen.StraysHeld"/>): those left out, with the <c>queries</c> entry that would
    /// type them, and those read as the database's, by folder, with the database scripts below it that keep the folder in the source.
    /// </summary>
    /// <param name="output">Where it is said.</param>
    /// <param name="strays">The choice, which holds them.</param>
    /// <param name="workingDirectory">What the paths are shown relative to.</param>
    /// <param name="shown">The source as shown, when it is one folder; null when it is a list.</param>
    /// <param name="listed">The folders listed as the source, when it is a list.</param>
    private static void Stray(TextWriter output, Chosen strays, string workingDirectory, string? shown, IReadOnlyList<string> listed)
    {
        string Shown(string path) => Visible.Of(DisplayPath.From(workingDirectory, path));

        if (strays.StraysLeftOut.Count > 0)
        {
            var files = strays.StraysLeftOut.Select(Shown).ToList();
            var one = files.Count == 1;
            var queries = one ? Quoted(files[0]) : $"[{string.Join(", ", files.Select(Quoted))}]";
            output.WriteLine(one
                ? $"{files[0]} adds nothing to the database, so the source leaves it out. If it is the application's query file, add \"queries\": {queries} to type it."
                : $"{EnglishList.Of(files)} add nothing to the database, so the source leaves them out. If they are the application's query files, add \"queries\": {queries} to type them.");
        }

        foreach (var held in strays.StraysHeld.GroupBy(FolderOf, StringComparer.Ordinal))
        {
            var files = held.Select(Shown).ToList();
            var adding = strays.StraysHolding.Where(d => Below(d, held.Key)).Select(Shown).ToList();
            var one = files.Count == 1;
            var holder = shown ?? Shown(listed.First(l => Below(held.Key, l)));
            output.WriteLine(
                (one
                    ? $"{files[0]} adds nothing to the database, but {holder} holds it, so it is read as the database's script and not typed. Its folder"
                    : $"{EnglishList.Of(files)} add nothing to the database, but {holder} holds them, so they are read as the database's scripts and not typed. Their folder")
                + $" can't be left out, since {EnglishList.Of(adding)}, below it, {(adding.Count == 1 ? "adds" : "add")} to the database.");
        }
    }

    private static int Refused(TextWriter error, params Diagnostic[] diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            error.WriteLine(diagnostic);
        return HaruspexCli.ExitCodes.Usage;
    }

    /// <summary>
    /// The source chosen, and the folders of <c>.sql</c> files that add nothing to the database: those it leaves out, and those it holds and
    /// so reads as the database's scripts.
    /// </summary>
    /// <param name="Source">The source.</param>
    /// <param name="LeftOut">The folders of queries alone that the source leaves out.</param>
    /// <param name="ReadAsDatabase">The folders of queries alone that the source holds.</param>
    /// <param name="Parts">
    /// When it holds some and they can't be left out by listing folders, where the database's scripts are: each folder directly below
    /// the source that holds some, and each script directly in it, one of which is a script.
    /// </param>
    /// <param name="Folders">
    /// When the source would hold some and the database's scripts are all in folders directly below it, those folders: the source is
    /// then the list of them, which leaves the others out (docs/decisions/0127-a-source-may-be-several-folders.md).
    /// </param>
    private readonly record struct Chosen(string Source, EquatableArray<string> LeftOut, EquatableArray<string> ReadAsDatabase, EquatableArray<string> Parts, EquatableArray<string> Folders)
    {
        /// <summary>
        /// The query scripts whose folder holds no database script but has one below it, which no folder of <see cref="LeftOut"/> or
        /// <see cref="ReadAsDatabase"/> names, so each is named as itself: here those outside what the source reads, in ordinal order.
        /// </summary>
        public EquatableArray<string> StraysLeftOut { get; init; } = [];

        /// <summary>Such query scripts that the source reads as the database's, in ordinal order.</summary>
        public EquatableArray<string> StraysHeld { get; init; } = [];

        /// <summary>The database scripts below the folders of <see cref="StraysHeld"/>, which keep them in the source, in ordinal order.</summary>
        public EquatableArray<string> StraysHolding { get; init; } = [];
    }

    /// <summary>The source the command line names, which must be there.</summary>
    private static Outcome<Chosen> Given(string source, string workingDirectory)
    {
        var path = Path.GetFullPath(Path.Combine(workingDirectory, source));
        return File.Exists(path) || Directory.Exists(path)
            ? new Succeeded<Chosen>(new Chosen(path, [], [], [], []))
            : ModuleRun.Refused<Chosen>(CliCodes.SourceNotFound, $"not found: {source}");
    }

    /// <summary>
    /// The database below the working directory: its one project file; else its one DACPAC, leaving out the system databases' a
    /// build copies beside it; else a folder of <c>.sql</c> files (<see cref="Scripts"/>). Hidden folders and <c>node_modules</c> are
    /// passed over, and <c>bin</c> and <c>obj</c>, a build's, except that a DACPAC is looked for in <c>bin</c>, where a build writes
    /// it. Two of the kind it settles on are a choice it leaves to the developer.
    /// </summary>
    private static Outcome<Chosen> Find(string workingDirectory)
    {
        var files = Files(workingDirectory, 0).ToList();
        var projects = files.Where(f => !f.InBuild && SqlProject.IsProjectFile(f.Path)).Select(f => f.Path).ToList();
        var packages = files.Where(f => SqlProject.IsPackage(f.Path) && !SystemPackages.Contains(Path.GetFileName(f.Path))).Select(f => f.Path).ToList();
        var scripts = files.Where(f => !f.InBuild && f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();

        foreach (var (kind, candidates) in new[] { ("project files", projects), ("DACPACs", packages) })
        {
            if (candidates.Count == 1)
                return new Succeeded<Chosen>(new Chosen(candidates[0], [], [], [], []));
            if (candidates.Count > 1)
            {
                var named = EnglishList.Of([.. candidates.Select(c => DisplayPath.From(workingDirectory, c))]);
                return ModuleRun.Refused<Chosen>(CliCodes.SourceAmbiguous,
                    $"found {candidates.Count} {kind}, {named}, and can't tell which is the application's. Name one: haruspex init {DisplayPath.From(workingDirectory, candidates[0])}.");
            }
        }

        if (scripts.Count > 0)
            return new Succeeded<Chosen>(Scripts(workingDirectory, scripts));

        return ModuleRun.Refused<Chosen>(CliCodes.NoSourceFound,
            $"found no .sqlproj, .dacpac or .sql file in the working directory or the four folders below it. Run it beside the database's files, or name them: haruspex init path/to/Sales.sqlproj.");
    }

    /// <summary>
    /// The folder of <paramref name="scripts"/> to read as the database: the deepest that holds every script the folder reader takes
    /// something from (<see cref="QueryOnlyScripts"/>); or, when it takes nothing from any, the deepest that holds them all.
    /// </summary>
    /// <remarks>
    /// The folder that holds every script would be wrong whenever scripts are in two places, as in an application that keeps its
    /// database in <c>database</c> and its query files in <c>queries</c>: a folder source reads every <c>.sql</c> file below it, so
    /// the query files would be read as the database's scripts, and with them whatever else is below the application's folder
    /// that this search passes over but a folder source reads (installed packages, hidden folders, a build's output). A script the
    /// reader takes nothing from can be left out without changing what is read; any other (a definition, an <c>ALTER</c>, a
    /// <c>NOCHECK</c>, a bulk load) may change what is claimed, so the source holds every one, and claims no more than the database
    /// it describes. The folders of the others are returned, for the developer to name as query files if they are: those the
    /// source leaves out, and those it holds, which it reads as the database's scripts and so doesn't type, with what made it hold them.
    /// </remarks>
    private static Chosen Scripts(string workingDirectory, List<string> scripts)
    {
        var queries = scripts.Where(QueryOnlyScripts.Is).ToHashSet(StringComparer.Ordinal);
        var database = scripts.Where(s => !queries.Contains(s)).ToList();
        if (database.Count == 0)
            return new Chosen(Common([.. scripts.Select(Path.GetDirectoryName).OfType<string>()]), [], [], [], []);

        var source = Common([.. database.Select(Path.GetDirectoryName).OfType<string>()]);

        // A query script whose folder holds no database script but has one below it: the folder can't be named, as below, so the
        // script is named as itself, left out or held by what the source reads.
        var stray = queries
            .Where(q => FolderOf(q) is var f
                && !database.Any(d => FolderOf(d) == f)
                && database.Any(d => Below(d, f)))
            .Order(StringComparer.Ordinal)
            .ToList();
        Chosen With(Chosen chosen, IReadOnlyList<string> read)
        {
            var held = stray.Where(q => read.Any(r => Below(q, r))).ToList();
            return chosen with
            {
                StraysLeftOut = [.. stray.Where(q => !held.Contains(q, StringComparer.Ordinal))],
                StraysHeld = [.. held],
                StraysHolding = [.. database.Where(d => held.Any(q => Below(d, FolderOf(q)))).Order(StringComparer.Ordinal)],
            };
        }

        // Each query script's folder that holds no database script, at it or below, widened to the highest that holds none, below
        // the working directory and, for one inside the source, below the source; so queries/ is named once for every file in it
        // and its folders. A script beside the database's own, as a CREATE SCHEMA is beside its tables, is part of that folder.
        var folders = queries
            .Select(q => Path.GetDirectoryName(q) ?? workingDirectory)
            .Where(f => !database.Any(d => Below(d, f)))
            .Select(f => Highest(f, Below(f, source) ? source : workingDirectory))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var inside = folders.Where(f => Below(f, source)).ToList();
        if (inside.Count == 0)
            return With(new Chosen(source, [.. folders], [], [], []), [source]);

        // Where the database's scripts are, which made the source hold them: each folder directly below the source that holds some,
        // and each script directly in it. None of them is taken as the database's own, since which it is isn't known.
        var parts = database
            .Select(d => Top(Path.GetRelativePath(source, d)) is { Length: > 0 } top ? Path.Join(source, top) : d)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // When every part is a folder, the source is the list of them, and a folder of queries beside them is left out as one outside
        // the source is. A script directly in the source can't be listed (a list names folders), so then the advice stays.
        if (parts.All(p => !database.Contains(p, StringComparer.Ordinal)))
        {
            var within = inside.Where(f => parts.Any(p => Below(f, p))).ToList();
            return With(new Chosen(source, [.. folders.Where(f => !within.Contains(f, StringComparer.Ordinal))], [.. within], [], [.. parts]), parts);
        }

        return With(new Chosen(source, [.. folders.Where(f => !Below(f, source))], [.. inside], [.. parts], []), [source]);

        static string Top(string path)
        {
            var at = path.IndexOfAny(['/', Path.DirectorySeparatorChar]);
            return at < 0 ? "" : path[..at];
        }

        string Highest(string folder, string limit)
        {
            while (folder != limit && Path.GetDirectoryName(folder) is { } parent && parent != limit && Below(parent, limit)
                && !database.Any(d => Below(d, parent)))
            {
                folder = parent;
            }

            return folder;
        }
    }

    /// <summary>A file found below the working directory, and whether it is in a build's folder (<c>bin</c>, <c>obj</c>).</summary>
    private readonly record struct Found(string Path, bool InBuild);

    /// <summary>Every file below <paramref name="folder"/> to <see cref="Depth"/>, in ordinal order, so the same folder gives the same choice on every machine.</summary>
    private static IEnumerable<Found> Files(string folder, int depth, bool inBuild = false)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options).Order(StringComparer.Ordinal))
            yield return new Found(file, inBuild);
        if (depth == Depth)
            yield break;
        foreach (var below in Directory.EnumerateDirectories(folder, "*", options).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(below);
            if (name.StartsWith('.') || name == "node_modules")
                continue;
            foreach (var file in Files(below, depth + 1, inBuild || name is "bin" or "obj"))
                yield return file;
        }
    }

    /// <summary>The deepest folder that holds every one of <paramref name="folders"/>, itself or below.</summary>
    private static string Common(List<string> folders)
    {
        var common = folders[0];
        foreach (var folder in folders.Skip(1))
        {
            while (!Below(folder, common))
                common = Path.GetDirectoryName(common) ?? common;
        }

        return common;
    }

    /// <summary>The folder a file found below the working directory is in; a path found there always has one.</summary>
    private static string FolderOf(string file) => Path.GetDirectoryName(file) ?? file;

    /// <summary>Whether <paramref name="folder"/> is <paramref name="root"/> or below it.</summary>
    private static bool Below(string folder, string root) =>
        folder == root || folder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>The schemas the source's procedures are in, in ordinal order, and the errors reading it found.</summary>
    private static (List<string> Schemas, List<Diagnostic> Errors) Schemas(string source)
    {
        var project = SqlProject.IsPackage(source) ? SqlProject.LoadPackage(source, [])
            : SqlProject.IsProjectFile(source) ? SqlProject.LoadProject(source, [])
            : SqlProject.Load(source);
        return ([.. project.Procedures.Select(p => p.Schema).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. project.Diagnostics.Where(d => d.IsError)]);
    }

    /// <summary>The schemas the procedures of the folders listed are in, read as one database, and the errors reading them found.</summary>
    private static (List<string> Schemas, List<Diagnostic> Errors) Schemas(string directory, List<string> folders)
    {
        var project = SqlProject.LoadFolders(directory, folders);
        return ([.. project.Procedures.Select(p => p.Schema).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. project.Diagnostics.Where(d => d.IsError)]);
    }

    /// <summary>The starter config file, with a comment on each setting saying what to change; the source one path, or a list of folders.</summary>
    private static string Text(List<string> source, string @out, List<string> schemas, string configSchema)
    {
        var text = new StringBuilder();
        text.Append("// haruspex's settings, written by haruspex init. Each is described in\n");
        text.Append($"// {HaruspexSite.Docs}configuration\n");
        text.Append("// where \"The settings you probably want\" says which most projects need next: the callers' session,\n");
        text.Append("// a DACPAC's build, the collation of a folder of scripts.\n");
        text.Append("{\n");
        text.Append($"  \"$schema\": {Quoted(configSchema)},\n");
        text.Append(source.Count == 1 ? $"  \"source\": {Quoted(source[0])},\n" : $"  \"source\": [{string.Join(", ", source.Select(Quoted))}],\n");
        text.Append($"  \"out\": {Quoted(@out)},\n");
        text.Append("  // Which procedures to type, by schema: every one, to start. Narrow each \"include\" to those the application calls.\n");
        text.Append("  \"schemas\": {");
        if (schemas.Count == 0)
        {
            text.Append("},\n");
        }
        else
        {
            text.Append('\n');
            foreach (var schema in schemas)
                text.Append($"    {Quoted(schema)}: {{ \"include\": [\"*\"] }},\n");
            text.Append("  },\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    /// <summary>A string as JSON writes it, with a name's own characters kept rather than escaped.</summary>
    private static string Quoted(string value) => $"\"{JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"";
}
