namespace Haruspex.Tests.Sources;

/// <summary>The two kinds of link to a folder a test can make (<see cref="FolderLink"/>).</summary>
public enum FolderLinkKind
{
    /// <summary>A symbolic link, on every platform. Windows makes one only with Developer Mode on or for an administrator.</summary>
    Symbolic,

    /// <summary>A junction, Windows's own link to a folder, which any user may make.</summary>
    Junction,
}
