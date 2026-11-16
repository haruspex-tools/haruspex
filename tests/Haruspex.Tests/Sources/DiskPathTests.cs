using Haruspex.SqlServer.Sources.Projects;

namespace Haruspex.Tests.Sources;

/// <summary>
/// <see cref="DiskPath.Real"/>: where a folder really is, as walking a project's folder needs it to enter each folder once, on
/// Windows, macOS and Linux.
/// </summary>
public class DiskPathTests
{
    [Fact]
    public void A_root_is_real_as_it_is_and_is_never_asked_for_a_link()
    {
        // Windows can't read a link at a drive's root: asking it threw DirectoryNotFoundException for 'C:\' (7w).
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root, DiskPath.Real(root));
    }

    [Fact]
    public void A_share_s_root_on_windows_is_real_as_it_is_whatever_the_case_of_its_names()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("a share's root, \\\\server\\share, is a root only on Windows");

        // A root is taken as written, without looking for the server, so this needs no network.
        Assert.Equal(@"\\FILES\TEAM", DiskPath.Real(@"\\files\team"));
    }

    [Fact]
    public void A_folder_is_real_under_its_own_name()
    {
        using var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "db", "Tables"));

        Assert.Equal(Path.Combine(DiskPath.Real(folder.Path)!, "db", "Tables"), DiskPath.Real(Path.Combine(folder.Path, "db", "Tables")));
    }

    [Fact]
    public void A_folder_written_in_another_case_is_real_in_the_case_it_has_on_disk()
    {
        using var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "db", "Tables"));
        var written = Path.Combine(folder.Path, "DB", "tables");
        if (!Directory.Exists(written))
            Assert.Skip("this file system tells case apart, so the folder written in another case isn't there");

        Assert.Equal(Path.Combine(DiskPath.Real(folder.Path)!, "db", "Tables"), DiskPath.Real(written));
    }

    [Theory]
    [InlineData(FolderLinkKind.Symbolic)]
    [InlineData(FolderLinkKind.Junction)]
    public void A_folder_through_a_link_is_real_where_the_link_leads(FolderLinkKind kind)
    {
        using var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "shared", "inner"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "p"));
        FolderLink.Make(kind, Path.Combine(folder.Path, "p", "elsewhere"), Path.Combine(folder.Path, "shared"));

        var real = Path.Combine(DiskPath.Real(folder.Path)!, "shared");
        Assert.Equal(real, DiskPath.Real(Path.Combine(folder.Path, "p", "elsewhere")));
        Assert.Equal(Path.Combine(real, "inner"), DiskPath.Real(Path.Combine(folder.Path, "p", "elsewhere", "inner")));
    }

    [Theory]
    [InlineData(FolderLinkKind.Symbolic)]
    [InlineData(FolderLinkKind.Junction)]
    public void A_link_to_a_folder_that_isn_t_there_is_real_where_it_leads(FolderLinkKind kind)
    {
        // Asked for the end of the chain, .NET threw for the folder that isn't there (observed on Windows).
        using var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "p"));
        FolderLink.Make(kind, Path.Combine(folder.Path, "p", "dangling"), Path.Combine(folder.Path, "gone"));

        Assert.Equal(Path.Combine(DiskPath.Real(folder.Path)!, "gone"), DiskPath.Real(Path.Combine(folder.Path, "p", "dangling")));
    }

    [Theory]
    [InlineData(FolderLinkKind.Symbolic)]
    [InlineData(FolderLinkKind.Junction)]
    public void Links_that_lead_round_in_a_circle_are_nowhere(FolderLinkKind kind)
    {
        using var folder = new TempDirectory();
        FolderLink.Make(kind, Path.Combine(folder.Path, "a"), Path.Combine(folder.Path, "b"));
        FolderLink.Make(kind, Path.Combine(folder.Path, "b"), Path.Combine(folder.Path, "a"));

        Assert.Null(DiskPath.Real(Path.Combine(folder.Path, "a")));
    }

    [Fact]
    public void A_junction_read_through_a_share_is_real_where_it_leads()
    {
        // Asked for the end of the chain through \\localhost\C$, .NET gave UNC\localhost\… under the working folder (observed on Windows).
        using var folder = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(folder.Path, "shared"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "p"));
        FolderLink.Make(FolderLinkKind.Junction, Path.Combine(folder.Path, "p", "elsewhere"), Path.Combine(folder.Path, "shared"));

        var shared = LocalShare.Of(folder.Path);

        Assert.Equal(Path.Combine(DiskPath.Real(folder.Path)!, "shared"), DiskPath.Real(Path.Combine(shared, "p", "elsewhere")));
    }

    [Fact]
    public void The_temp_folder_is_real_with_every_link_above_it_followed()
    {
        // On macOS /var is a link to /private/var, so the temp folder's real path differs from the one written.
        var temp = Path.GetFullPath(Path.GetTempPath());
        var real = DiskPath.Real(temp);

        Assert.True(Directory.Exists(real));
        Assert.Equal(real, DiskPath.Real(real));
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal))
            Assert.StartsWith("/private/var/", real, StringComparison.Ordinal);
    }
}
