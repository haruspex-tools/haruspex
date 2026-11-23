using Haruspex.Diagnostics;
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
/// is relative; a path rooted the other platform's way (<c>C:\build\…</c> or <c>\build\…</c> read on Linux) as
/// recorded, since it can't be a place here and joining it to a folder would make one that never exists.</item>
/// </list>
/// A recorded path that names a share or a device (<c>\\host\share\…</c>, <c>//host/share/…</c>, <c>\\?\…</c>,
/// <c>\\.\…</c>, <c>\??\…</c>) is never looked for, on any platform: the warning quotes it and says so
/// (<see cref="NamesShareOrDevice"/>).
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
    public static IReadOnlyList<string> Candidates(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders) =>
        [.. Made(reference, referrer, searchFolders).Concat(RecordedPlace(reference, referrer) is { } recorded ? [recorded] : []).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The places looked, as the warning that the package isn't found lists them: each place haruspex made, beside the referrer or
    /// in a folder of references, as haruspex prints a path (<see cref="DiagnosticFormatter.PathAsShown"/>), and then the path
    /// the package recorded, exactly as recorded, unless a place already listed is the one it names; or, when it names a share or
    /// a device, saying it wasn't looked for.
    /// </summary>
    /// <remarks>
    /// The recorded path is quoted, as a project's paths are quoted from the project (<c>..\archive\loans.sql</c>), because it is
    /// the build machine's path, which a reader knows from their build, and because then the warning reads the same wherever
    /// haruspex runs. Made full here it wouldn't: on Windows, <c>/tmp/…</c> recorded by a build on Linux names a folder on the
    /// current drive, <c>D:\tmp\…</c>, which is where it is looked for, but which no build wrote.
    /// </remarks>
    public static IReadOnlyList<string> Shown(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders)
    {
        var made = Made(reference, referrer, searchFolders).Distinct(StringComparer.Ordinal).ToList();
        var shown = made.Select(DiagnosticFormatter.PathAsShown).ToList();
        if (NamesShareOrDevice(reference.FileName))
            shown.Add($"and not at {reference.FileName}, which names a share or a device, where haruspex never looks");
        else if (RecordedPlace(reference, referrer) is { } recorded && !made.Contains(recorded, StringComparer.Ordinal))
            shown.Add(reference.FileName);
        return shown;
    }

    /// <summary>The first place a file is, or null when none.</summary>
    public static string? Find(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders) =>
        Candidates(reference, referrer, searchFolders).FirstOrDefault(File.Exists);

    /// <summary>The places looked beside the referrer and in the config's folders, full, in order.</summary>
    private static IEnumerable<string> Made(PackageReference reference, string referrer, IReadOnlyList<string> searchFolders)
    {
        var beside = Beside(referrer);
        var names = new[] { DacPackage.FileNameOf(reference.FileName), reference.LogicalName }.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        IEnumerable<string> places =
        [
            .. names.Select(name => Path.Join(beside, name)),
            .. searchFolders.SelectMany(folder => names.Select(name => Path.Join(folder, name))),
        ];
        return places.Select(Path.GetFullPath);
    }

    /// <summary>
    /// The place the recorded path names here: made full against the referrer's folder when relative, or as recorded when rooted
    /// the other platform's way; or null when it names a share or a device, which is never looked for.
    /// </summary>
    private static string? RecordedPlace(PackageReference reference, string referrer)
    {
        var recorded = reference.FileName;
        if (NamesShareOrDevice(recorded))
            return null;
        if (DiskPath.IsRootedElsewhere(recorded))
            return recorded;
        var here = recorded.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(here) ? here : Path.Join(Beside(referrer), here));
    }

    /// <summary>
    /// Whether <paramref name="recorded"/> begins the way .NET on Windows recognises a share or a device: with two separators,
    /// of either kind (<c>\\host\share\…</c>, <c>//host/share/…</c>, <c>\\?\UNC\…</c>, <c>\\?\C:\…</c>, <c>\\.\pipe\…</c>), or with
    /// <c>\??\</c>, the NT object manager's own prefix (<c>\??\UNC\host\share\…</c>, <c>\??\GLOBALROOT\Device\Mup\…</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Windows, asking whether a file on a share exists connects to the share's host, and Windows offers that host the user's
    /// credentials. The recorded path is written into a package by whoever built it, where no one sees it without unzipping the
    /// package, and packages are followed transitively, a NuGet package's included; so following it would let a stranger's package
    /// choose a host for haruspex to contact, against <c>SECURITY.md</c>'s promise that it makes no network connection. A
    /// device path may also name a share (<c>\\?\UNC\…</c>), or something that isn't a file at all, such as a pipe.
    /// </para>
    /// <para>
    /// These are the two beginnings .NET's own path code treats as other than a drive or a folder, its <c>IsUnc</c> and
    /// <c>IsDevice</c> (which takes <c>\??\</c> only as written, with backslashes): so the rule is complete for the paths .NET
    /// hands to Windows. Every other recorded path is a drive path, rooted or relative, or relative to the drive in use; observed
    /// on Windows, <c>/??/…</c>, <c>\??/…</c> and a path with a leading space are local.
    /// </para>
    /// <para>
    /// The test is on the text, the same on every platform, so that the rule can be tested anywhere. Elsewhere <c>//host/share</c>
    /// is the local folder <c>/host/share</c>, and passing it by costs only the one place a build on another machine left a
    /// package, which is almost never there; the copy beside the referrer, or in a folder of <c>references</c>, is the one to
    /// read. A project file can't reach a share this way: each path it gives is joined to its own folder.
    /// </para>
    /// </remarks>
    private static bool NamesShareOrDevice(string recorded) =>
        (recorded.Length >= 2 && (recorded[0] is '\\' or '/') && (recorded[1] is '\\' or '/'))
        || recorded.StartsWith(@"\??\", StringComparison.Ordinal);

    /// <summary>The folder of the package that records the reference.</summary>
    private static string Beside(string referrer) => Path.GetDirectoryName(Path.GetFullPath(referrer)) ?? ".";
}
