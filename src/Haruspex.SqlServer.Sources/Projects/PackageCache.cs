namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// The NuGet packages restored on this machine, where a <c>Microsoft.Build.Sql</c> build finds the DACPAC a
/// <c>PackageReference</c> carries: <c>tools/&lt;name&gt;.dacpac</c> in the package's folder (its <c>Sdk.targets</c>,
/// <c>ResolveDacpacPackageReferences</c>). A package without one, such as a code-analysis rules package, adds nothing to the
/// database, so a reference to it says nothing.
/// </summary>
/// <param name="folder">The cache's folder: <c>NUGET_PACKAGES</c>, else <c>.nuget/packages</c> in the user's home, as NuGet finds it.</param>
internal sealed class PackageCache(string folder)
{
    /// <summary>The cache NuGet uses on this machine.</summary>
    public static PackageCache Default { get; } = new(
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } set
            ? set
            : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));

    /// <summary>
    /// Whether the package <paramref name="id"/> holds a DACPAC: in the version given, when the cache has it, or else in any
    /// version it has; null when the cache has the package in no version, so it isn't known.
    /// </summary>
    public bool? HoldsDacpac(string id, string? version)
    {
        // NuGet names a package's folder, and its versions', in lower case; found ignoring case, as every path here is.
        if (DiskPath.Locate(Path.Join(folder, id), id, out _) is not { } package || !Directory.Exists(package))
            return null;
        var exact = version is { Length: > 0 } ? DiskPath.Locate(Path.Join(package, version), version, out _) : null;
        var versions = exact is not null && Directory.Exists(exact) ? [exact] : Directory.EnumerateDirectories(package).ToList();
        if (versions.Count == 0)
            return null;
        return versions.Any(v => Directory.Exists(Path.Join(v, "tools")) && Directory.EnumerateFiles(Path.Join(v, "tools"), "*.dacpac").Any());
    }
}
