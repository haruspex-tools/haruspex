using System.Diagnostics;

namespace Haruspex.Tests.Sources;

/// <summary>Makes a link to a folder, or skips the test where this machine can't make that kind.</summary>
internal static class FolderLink
{
    /// <summary>Makes <paramref name="link"/> a link of <paramref name="kind"/> to the folder <paramref name="target"/>.</summary>
    public static void Make(FolderLinkKind kind, string link, string target)
    {
        if (kind == FolderLinkKind.Symbolic)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (IOException e) when (OperatingSystem.IsWindows())
            {
                // Without Developer Mode, Windows refuses a symbolic link to a user who isn't an administrator.
                Assert.Skip($"this machine doesn't let this user make a symbolic link: {e.Message}");
            }

            return;
        }

        if (!OperatingSystem.IsWindows())
            Assert.Skip("a junction is Windows's own kind of link");

        // .NET makes no junction, so cmd's mklink does.
        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start");
        var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"mklink /J failed: {said}");
    }
}
