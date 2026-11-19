using Haruspex.Cli.Output;
using Haruspex.Config;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Cli;

/// <summary>
/// The line of SQL a location in the IR points at, when it is at hand: what <c>haruspex explain</c> prints under a reference. A
/// location is relative to the source it was inferred from (a folder of scripts, a project's folder, the config file's folder for
/// a query file), or names a package and an object in it. A file is shown when it is there; a package only when its SHA-256 is
/// the one the IR recorded, so the line is the one the location was written from. Anything else gives null, and the reference
/// is printed alone: an application's repository often has the IR and not the SQL, which is no error.
/// </summary>
/// <param name="document">The IR, for each package's recorded SHA-256.</param>
/// <param name="config">The config file read, or null: where the source and the baseline are.</param>
/// <param name="workingDirectory">Where the command runs, the last place a relative file is looked for.</param>
internal sealed class ExplainSources(IrDocument document, HaruspexConfig? config, string workingDirectory)
{
    private const string Package = ".dacpac/";

    /// <summary>The line <paramref name="at"/> points at, in the candidate's source or, for <paramref name="baseline"/>, the baseline's.</summary>
    public string? Line(string at, bool baseline = false)
    {
        if (!SourceLocation.TryParse(at, out var location))
            return null;
        var source = baseline ? config?.Baseline : config?.Source;
        var package = location.File.IndexOf(Package, StringComparison.OrdinalIgnoreCase);
        if (package >= 0)
        {
            var name = location.File[..(package + Package.Length - 1)];
            var obj = location.File[(package + Package.Length)..];
            return Sha256(name, baseline) is { } sha
                ? Packages(name, source).Select(path => PackageLines.Line(path, sha, obj, location.Line)).FirstOrDefault(line => line is not null)
                : null;
        }

        foreach (var root in Roots(source))
        {
            var path = Path.GetFullPath(Path.Combine(root, location.File));
            if (Read(path, location.Line) is { } line)
                return line;
        }

        return null;
    }

    /// <summary>The source a side of a combined field was read from, by the package's name or the path the config file gives.</summary>
    public string SourceOf(bool baseline)
    {
        var source = baseline ? document.Source.Baseline : document.Source;
        var path = baseline ? config?.Baseline : config?.Source;
        return source?.Package?.Name ?? (path is null ? (baseline ? "the baseline" : "the source") : DisplayPath.From(workingDirectory, path));
    }

    /// <summary>The SHA-256 the IR records for the package <paramref name="name"/>, the source's own or one it refers to.</summary>
    private string? Sha256(string name, bool baseline)
    {
        var source = baseline ? document.Source.Baseline : document.Source;
        if (source is null)
            return null;
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

    private static string? Read(string path, int line)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
            return line >= 1 && line <= lines.Length ? lines[line - 1] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
