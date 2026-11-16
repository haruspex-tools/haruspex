namespace Haruspex.Tests.Sources;

/// <summary>A local folder read through a share, as <c>\\localhost\C$\…</c>, or the test skipped where it can't be.</summary>
internal static class LocalShare
{
    /// <summary>The full local path <paramref name="local"/> through its drive's administrative share on this machine.</summary>
    public static string Of(string local)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("a share's path, \\\\server\\share\\…, is Windows's");
        var root = Path.GetPathRoot(local)!;
        var share = $@"\\localhost\{root[0]}$";
        if (!Directory.Exists(share + @"\"))
            Assert.Skip($"{share} can't be reached: this machine doesn't share its drive to its own user");
        return share + local[(root.Length - 1)..];
    }
}
