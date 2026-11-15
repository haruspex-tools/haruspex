namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>What a <see cref="DatabaseReference"/> names, which decides how it is read and what to do when it isn't found.</summary>
internal enum DatabaseReferenceKind
{
    /// <summary>A database project (<c>ProjectReference</c>), read from its own project file.</summary>
    Project,

    /// <summary>A DACPAC named by its path (<c>ArtifactReference</c>).</summary>
    Artifact,

    /// <summary>The DACPAC a NuGet package holds (<c>PackageReference</c>), where a restore puts it.</summary>
    Package,

    /// <summary>A system database's DACPAC Visual Studio installs, named under <c>$(DacPacRootPath)</c>, which only Visual Studio defines.</summary>
    VisualStudio,
}
