namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// What made a package, as far as its <c>Origin.xml</c> says. A build and an extract record different things, and each leaves
/// out something the other has, so a reader that can't tell which made a package takes, for each fact, the side that claims less
/// (<see cref="DacPackage.Origin"/>).
/// </summary>
internal enum PackageOrigin
{
    /// <summary>Neither is said: no <c>Origin.xml</c>, or one naming no server and another tool than the build task, such as DacFx's API.</summary>
    Unknown,

    /// <summary>Built from a project's scripts, by DacFx's build task.</summary>
    Built,

    /// <summary>Extracted from a database.</summary>
    Extracted,
}
