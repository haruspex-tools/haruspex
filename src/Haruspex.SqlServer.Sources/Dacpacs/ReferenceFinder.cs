using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// Where a referenced package is looked for, in order, and the first file found wins
/// (<c>docs/decisions/0040-a-dacpac-is-read-with-the-packages-of-its-database.md</c>):
/// <list type="number">
/// <item>beside the package that refers to it, under the file name it recorded and then under its logical name: where a build
/// leaves every package it references, in the referring one's <c>bin</c> folder;</item>
/// <item>in each folder of the config file's <c>references</c>, in order, under the same names;</item>
/// <item>at the path recorded when the referring package was built, made full against the referring package's folder when it
/// is relative; a path rooted the other platform's way (<c>C:\build\…</c>, <c>\\server\share\…</c> read on Linux) as
/// recorded, since it can't be a place here and joining it to a folder would make one that never exists.</item>
/// </list>
/// </summary>
/// <remarks>
/// The recorded path comes last because it is a path on the machine that built the package (observed: an absolute path under
/// the build's working folder). On another machine it usually doesn't exist, and where it happens to, it may be an older build
/// than the copy beside the package, which must not win in silence. Each package's SHA-256 in the IR shows which was read.
/// </remarks>
internal static class ReferenceFinder
{
    /// <summary>The places looked, in order, each once.</summary>
    /// <param name="reference">The reference.</param>
    /// <param name="referrer">The path of the package that records it.</param>
    /// <param name="searchFolders">The config file's <c>references</c> folders, full paths.</param>
    public static IReadOnlyList<string> Candidates(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders)
    {
        var beside = Path.GetDirectoryName(Path.GetFullPath(referrer)) ?? ".";
        var names = new[] { DacPackage.FileNameOf(reference.FileName), reference.LogicalName }.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        IEnumerable<string> places =
        [
            .. names.Select(name => Path.Join(beside, name)),
            .. searchFolders.SelectMany(folder => names.Select(name => Path.Join(folder, name))),
        ];
        var looked = places.Select(Path.GetFullPath);
        var recorded = reference.FileName;
        if (DiskPath.IsRootedElsewhere(recorded))
            return [.. looked.Append(recorded).Distinct(StringComparer.Ordinal)];
        var here = recorded.Replace('\\', Path.DirectorySeparatorChar);
        return [.. looked.Append(Path.GetFullPath(Path.IsPathRooted(here) ? here : Path.Join(beside, here))).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The first place a file is, or null when none.</summary>
    public static string? Find(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders) =>
        Candidates(reference, referrer, searchFolders).FirstOrDefault(File.Exists);
}
