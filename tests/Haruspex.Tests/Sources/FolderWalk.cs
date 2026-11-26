using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>The folders a walk from the root lists to find a path, as the platform's own path calls give them.</summary>
internal static class FolderWalk
{
    /// <summary>
    /// Each folder above <paramref name="path"/>, from the root down: what <c>DiskPath.Locate</c> lists to find it, name by name.
    /// </summary>
    /// <remarks>
    /// The folders come from <see cref="Path.GetFullPath(string)"/> and <see cref="Path.GetDirectoryName(string)"/>, never from
    /// splitting the path by hand. A test that split <c>…\p/Tables/t0.sql</c> on <see cref="Path.DirectorySeparatorChar"/> took
    /// <c>p/Tables/t0.sql</c> for one name on Windows, where <c>/</c> separates too, and left out the drive's root; so it expected
    /// 8 folders where the walk lists 10, each once (observed on Windows, 26 Nov 2026).
    /// </remarks>
    public static IReadOnlyList<string> To(string path)
    {
        var folders = new List<string>();
        for (var folder = Path.GetDirectoryName(Path.GetFullPath(path)); folder is not null; folder = Path.GetDirectoryName(folder))
            folders.Add(folder);
        folders.Reverse();
        return folders;
    }

    /// <summary>That <paramref name="listings"/> listed the folders <paramref name="expected"/> names, each once, and no others.</summary>
    /// <remarks>
    /// The names are compared ignoring case because the folders above a test's own come from the environment (the temporary
    /// folder), which may name them in another case than the disk's, and the walk gives the disk's. That loosens nothing: a folder
    /// listed twice under two spellings is still two entries, so the lists differ in length. Both are sorted, since the listings
    /// keep no order.
    /// </remarks>
    public static void Listed(IEnumerable<string> expected, FolderListings listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        Assert.Equal(
            expected.Order(StringComparer.OrdinalIgnoreCase), listings.Folders.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }
}
