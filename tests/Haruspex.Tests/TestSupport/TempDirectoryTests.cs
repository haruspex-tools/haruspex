namespace Haruspex.Tests.TestSupport;

public sealed class TempDirectoryTests
{
    // git writes its object files read-only, and Windows refuses to delete a read-only file (UnauthorizedAccessException, not
    // an IOException), so a test that clones into its folder failed in cleanup on Windows alone. Unix deletes it regardless.
    [Fact]
    public void A_folder_holding_a_read_only_file_is_deleted_and_disposing_it_throws_nothing()
    {
        var folder = new TempDirectory();
        var file = folder.File(Path.Combine("objects", "3e", "a514"), "x");
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);

        folder.Dispose();

        Assert.False(Directory.Exists(folder.Path));
    }
}
