using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;

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

    /// <summary>Runs the command: <paramref name="source"/>, if given, rather than what it finds.</summary>
    /// <returns>The exit code: 0; 2 when it finds a config file, no database or more than one; 1 when it can't write.</returns>
    public static int Run(string? source, TextWriter output, TextWriter error, string workingDirectory)
    {
        var config = Path.Join(workingDirectory, HaruspexConfig.FileName);
        if (File.Exists(config))
            return Refused(error, CliCodes.ConfigExists.At(new FileLocation(HaruspexConfig.FileName), "a config file is already here, and init doesn't write over one. Edit it, or remove it to start again."));

        var found = source is null ? Find(workingDirectory) : Given(source, workingDirectory);
        if (found is Failed<string> failed)
            return Refused(error, [.. failed.Diagnostics]);
        var chosen = ((Succeeded<string>)found).Value;

        // Relative to the config file's folder, as every path in the file is, even when it climbs out (../db): the file is
        // committed, and an absolute path would hold only on this machine. Only a source on another drive stays absolute.
        var shown = DiagnosticFormatter.PathAsShown(Path.GetRelativePath(workingDirectory, chosen));
        var schemas = Schemas(chosen);
        var @out = Directory.Exists(Path.Join(workingDirectory, "src")) ? "src/db" : "generated";
        try
        {
            // CreateNew, so a file that appeared since the check above is left as it is.
            using var file = new FileStream(config, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(Text(shown, @out, schemas));
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
        shown = Visible.Of(shown);
        output.WriteLine(schemas.Count == 0
            ? $"haruspex: wrote {HaruspexConfig.FileName}, which reads {shown}, where no procedure was found, so it selects none yet. Add a schema's \"include\", or \"queries\" for the application's query files, then run haruspex generate."
            : $"haruspex: wrote {HaruspexConfig.FileName}, which reads {shown} and types every procedure in {EnglishList.Of(shownSchemas)}, written to {@out}.");
        if (schemas.Count > 0)
        {
            output.WriteLine("Next, run haruspex generate. Then narrow each schema's \"include\" to the procedures the application calls.");

            // A team's format on save or pre-commit hook meets the generated files first, and check compares them as text.
            output.WriteLine($"Keep {@out} out of your formatter (in .prettierignore, or as \"!{@out}\" after \"**\" in Biome's files.includes): haruspex check fails on a file a formatter restyled.");
        }
        return HaruspexCli.ExitCodes.Success;
    }

    private static int Refused(TextWriter error, params Diagnostic[] diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            error.WriteLine(diagnostic);
        return HaruspexCli.ExitCodes.Usage;
    }

    /// <summary>The source the command line names, which must be there.</summary>
    private static Outcome<string> Given(string source, string workingDirectory)
    {
        var path = Path.GetFullPath(Path.Combine(workingDirectory, source));
        return File.Exists(path) || Directory.Exists(path)
            ? new Succeeded<string>(path)
            : ModuleRun.Refused<string>(CliCodes.SourceNotFound, $"not found: {source}");
    }

    /// <summary>
    /// The database below the working directory: its one project file; else its one DACPAC, leaving out the system databases' a
    /// build copies beside it; else the folder that holds every <c>.sql</c> file. Hidden folders and <c>node_modules</c> are passed
    /// over, and <c>bin</c> and <c>obj</c>, a build's, except that a DACPAC is looked for in <c>bin</c>, where a build writes it.
    /// Two of the kind it settles on are a choice it leaves to the developer.
    /// </summary>
    private static Outcome<string> Find(string workingDirectory)
    {
        var files = Files(workingDirectory, 0).ToList();
        var projects = files.Where(f => !f.InBuild && SqlProject.IsProjectFile(f.Path)).Select(f => f.Path).ToList();
        var packages = files.Where(f => SqlProject.IsPackage(f.Path) && !SystemPackages.Contains(Path.GetFileName(f.Path))).Select(f => f.Path).ToList();
        var scripts = files.Where(f => !f.InBuild && f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();

        foreach (var (kind, candidates) in new[] { ("project files", projects), ("DACPACs", packages) })
        {
            if (candidates.Count == 1)
                return new Succeeded<string>(candidates[0]);
            if (candidates.Count > 1)
            {
                var named = EnglishList.Of([.. candidates.Select(c => DisplayPath.From(workingDirectory, c))]);
                return ModuleRun.Refused<string>(CliCodes.SourceAmbiguous,
                    $"found {candidates.Count} {kind}, {named}, and can't tell which is the application's. Name one: haruspex init {DisplayPath.From(workingDirectory, candidates[0])}.");
            }
        }

        if (scripts.Count > 0)
            return new Succeeded<string>(Common([.. scripts.Select(Path.GetDirectoryName).OfType<string>()]));

        return ModuleRun.Refused<string>(CliCodes.NoSourceFound,
            $"found no .sqlproj, .dacpac or .sql file in the working directory or the four folders below it. Run it beside the database's files, or name them: haruspex init path/to/Sales.sqlproj.");
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

        static bool Below(string folder, string root) =>
            folder == root || folder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>The schemas the source's procedures are in, in ordinal order; none when it doesn't read, which generate will then say.</summary>
    private static List<string> Schemas(string source)
    {
        var project = SqlProject.IsPackage(source) ? SqlProject.LoadPackage(source, [])
            : SqlProject.IsProjectFile(source) ? SqlProject.LoadProject(source, [])
            : SqlProject.Load(source);
        return [.. project.Procedures.Select(p => p.Schema).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The starter config file, with a comment on each setting saying what to change.</summary>
    private static string Text(string source, string @out, List<string> schemas)
    {
        var text = new StringBuilder();
        text.Append("// haruspex's settings, written by haruspex init. Each is described in\n");
        text.Append("// https://github.com/sharpblue/haruspex/blob/main/docs/configuration.md\n");
        text.Append("{\n");
        text.Append($"  \"$schema\": {Quoted(ConfigSchema)},\n");
        text.Append($"  \"source\": {Quoted(source)},\n");
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

    /// <summary>The config file's published schema, which editors use to complete and check it.</summary>
    public const string ConfigSchema = "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.schema.json";

    /// <summary>A string as JSON writes it, with a name's own characters kept rather than escaped.</summary>
    private static string Quoted(string value) => $"\"{JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping)}\"";
}
