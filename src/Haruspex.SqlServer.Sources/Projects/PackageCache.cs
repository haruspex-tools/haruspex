using System.Globalization;
using System.Text.RegularExpressions;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The NuGet packages restored on this machine, where a <c>Microsoft.Build.Sql</c> build finds the DACPAC a
/// <c>PackageReference</c> carries: <c>tools/&lt;name&gt;.dacpac</c> in the folder the restore gave the package's version (its
/// <c>Sdk.targets</c>, <c>ResolveDacpacPackageReferences</c>, the same in 1.0.0 and 2.3.0).
/// </summary>
/// <param name="folder">The cache's folder: <c>NUGET_PACKAGES</c>, else <c>.nuget/packages</c> in the user's home, as NuGet finds it.</param>
internal sealed partial class PackageCache(string folder)
{
    /// <summary>The cache NuGet uses on this machine.</summary>
    public static PackageCache Default { get; } = new(
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } set
            ? set
            : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));

    /// <summary>The cache's folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>
    /// The folder NuGet restores <paramref name="id"/> at <paramref name="version"/> into, under <paramref name="cache"/> (this
    /// cache's folder unless the project names its own, <c>RestorePackagesPath</c>): the id and the version normalised as NuGet
    /// normalises them, in lower case. Null when <paramref name="version"/> isn't one version: a range or a floating version
    /// (<c>170.*</c>) is resolved by the restore against a feed, which isn't read (observed: <c>170.*</c> restored 170.0.4).
    /// </summary>
#pragma warning disable CA1308 // Lower case because NuGet names its folders so, not to compare.
    public static string? VersionFolder(string cache, string id, string version) =>
        Normalised(version) is { } normal ? Path.Join(cache, id.ToLowerInvariant(), normal) : null;
#pragma warning restore CA1308

    /// <summary>
    /// <paramref name="version"/> as NuGet names its folder: three parts at least, a fourth only when it isn't zero, each without
    /// leading zeros, in lower case, without build metadata; null for anything but one version.
    /// </summary>
    internal static string? Normalised(string version)
    {
        if (OneVersion().Match(version.Trim()) is not { Success: true } match)
            return null;
        var numbers = match.Groups["numbers"].Value.Split('.').Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToList();
        while (numbers.Count < 3)
            numbers.Add(0);
        if (numbers.Count == 4 && numbers[3] == 0)
            numbers.RemoveAt(3);
#pragma warning disable CA1308 // Lower case because NuGet names its folders so, not to compare.
        return string.Join('.', numbers) + match.Groups["release"].Value.ToLowerInvariant();
#pragma warning restore CA1308
    }

    [GeneratedRegex(@"^(?<numbers>\d{1,9}(\.\d{1,9}){0,3})(?<release>-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex OneVersion();
}
