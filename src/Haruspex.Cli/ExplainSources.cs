using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// The line of SQL a location in the IR points at, when it is at hand and is the one the location was written from: what
/// <c>haruspex explain</c> prints under a reference. A location is relative to the source it was inferred from (a folder of
/// scripts, a project's folder, the config file's folder for a query file), or names a package and an object in it. A script's
/// line is shown only when the file's hash is the one the IR recorded (<see cref="IrSource.Files"/>), and a package's only when
/// its SHA-256 is, so that a file edited since shows no line rather than the wrong one. A location that leaves the folder it is
/// looked for in, or is absolute, is never followed: the IR is a committed file anyone may edit. Anything else gives null, and
/// the reference is printed alone: an application's repository often has the IR and not the SQL, which is no error.
/// </summary>
/// <param name="document">The IR, for each file's and package's recorded hash.</param>
/// <param name="config">The config file read, or null: where the source and the baseline are.</param>
/// <param name="workingDirectory">Where the command runs, the last place a relative file is looked for.</param>
internal sealed class ExplainSources(IrDocument document, HaruspexConfig? config, string workingDirectory)
{
    private const string Package = ".dacpac/";

    /// <summary>The line <paramref name="at"/> points at, in the candidate's source or, for <paramref name="baseline"/>, the baseline's.</summary>
    public string? Line(string at, bool baseline = false)
    {
        if (!SourceLocation.TryParse(at, out var location) || (baseline ? document.Source.Baseline : document.Source) is not { } recorded)
            return null;
        var source = baseline ? config?.Baseline : config?.Source;
        var package = location.File.IndexOf(Package, StringComparison.OrdinalIgnoreCase);
        if (package >= 0)
        {
            var name = location.File[..(package + Package.Length - 1)];
            var obj = location.File[(package + Package.Length)..];
            return Path.GetFileName(name) == name && Sha256(recorded, name) is { } sha
                ? Packages(name, source).Select(path => PackageLines.Line(path, sha, obj, location.Line)).FirstOrDefault(line => line is not null)
                : null;
        }

        if (!recorded.Files.TryGetValue(location.File, out var hash) || Path.IsPathRooted(location.File))
            return null;
        foreach (var root in Roots(source))
        {
            var folder = Path.GetFullPath(root);
            var path = Path.GetFullPath(Path.Combine(folder, location.File));
            if (Inside(folder, path) && ScriptFiles.Sha256(path) == hash)
                return Read(path, location.Line);
        }

        return null;
    }

    /// <summary>
    /// The source a side of a combined field was read from, as the IR records it: its path as the config file named it, else a
    /// package by its name, and the side's role only for an IR that records neither (written before 0.25).
    /// </summary>
    public string SourceOf(bool baseline) =>
        (baseline ? document.Source.Baseline : document.Source) is { } source
            ? source.Path ?? source.Package?.Name ?? (baseline ? "the baseline" : "the source")
            : baseline ? "the baseline" : "the source";

    /// <summary>The SHA-256 the IR records for the package <paramref name="name"/>, the source's own or one it refers to.</summary>
    private static string? Sha256(IrSource source, string name)
    {
        if (source.Package is { } own && string.Equals(own.Name, name, StringComparison.OrdinalIgnoreCase))
            return own.Sha256;
        return source.References.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))?.Sha256;
    }

    /// <summary>Where a package of that name may be: the source itself, beside it, and in each folder the config file names.</summary>
    private IEnumerable<string> Packages(string name, string? source)
    {
        if (source is not null && string.Equals(Path.GetFileName(source), name, StringComparison.OrdinalIgnoreCase))
            yield return source;
        if (source is not null && Path.GetDirectoryName(source) is { } beside)
            yield return Path.Combine(beside, name);
        foreach (var folder in config?.References ?? [])
            yield return Path.Combine(folder, name);
        yield return Path.Combine(workingDirectory, name);
    }

    /// <summary>The folders a relative file is looked for in: the source's folder, the config file's, the working directory.</summary>
    private IEnumerable<string> Roots(string? source)
    {
        if (source is not null)
            yield return Directory.Exists(source) ? source : Path.GetDirectoryName(source) ?? source;
        if (config is not null)
            yield return config.Directory;
        yield return workingDirectory;
    }

    /// <summary>Whether <paramref name="path"/>, made full, is <paramref name="folder"/> or below it.</summary>
    private static bool Inside(string folder, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? Read(string path, int line)
    {
        try
        {
            var lines = SourceLines.Of(File.ReadAllText(path));
            return line >= 1 && line <= lines.Count ? lines[line - 1] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
