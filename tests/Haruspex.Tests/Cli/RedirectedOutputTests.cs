using System.Diagnostics;
using System.Text;

namespace Haruspex.Tests.Cli;

/// <summary>
/// The command run as a shell runs it, in a process of its own with its standard streams sent to files: what it writes there is
/// UTF-8 whatever the console's code page, and the console's code page is left as it was. The other tests pass the run a
/// <see cref="StringWriter"/>, which has no encoding to get wrong, so only a real process shows this.
/// </summary>
public class RedirectedOutputTests
{
    /// <summary>
    /// cmd sets the code page of the console it shares with haruspex to 437, which has no Cyrillic, runs haruspex twice with its
    /// output sent to files, and then records the console's output code page, which haruspex would change by setting
    /// <see cref="Console.OutputEncoding"/>. It is read through PowerShell because <c>chcp</c> shows the input code page, which that
    /// leaves alone (observed: after such a run, <c>chcp</c> said 437 and the output code page was 65001). Each name below is
    /// outside 437, so a stream written in the console's code page holds <c>?</c> for each of its letters (observed before
    /// the fix). The process gets a console of its own, without a window, so that the console of whoever runs the tests is never
    /// changed.
    /// </summary>
    [Fact]
    public void Redirected_output_is_utf8_whatever_the_console_s_code_page_which_is_left_as_it_was()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("a console's code page is Windows's: elsewhere a stream's encoding is the locale's, and no shell changes it");
        using var folder = new TempDirectory();
        folder.File("db/tables.sql", "CREATE TABLE dock.причал (id int NOT NULL PRIMARY KEY);");
        folder.File("db/procedures.sql", "CREATE PROCEDURE dock.list AS SELECT p.id, p.нет FROM dock.причал AS p;");
        folder.File("haruspex.json", """{ "source": "db", "out": "generated", "schemas": { "dock": { "include": ["*"] } } }""");
        var haruspex = Path.Combine(AppContext.BaseDirectory, "haruspex.exe");
        var commands = $"chcp 437 >nul & \"{haruspex}\" generate --format json >report.json & \"{haruspex}\" generate --verbose >nul 2>error.txt & powershell -NoProfile -NonInteractive -Command [Console]::OutputEncoding.CodePage >codepage.txt";
        var start = new ProcessStartInfo("cmd.exe", $"/d /s /c \"{commands}\"") { WorkingDirectory = folder.Path, UseShellExecute = false, CreateNoWindow = true };

        using (var cmd = Process.Start(start)!)
        {
            Assert.True(cmd.WaitForExit(TimeSpan.FromMinutes(2)), "cmd didn't finish in two minutes");
        }

        var report = File.ReadAllBytes(Path.Combine(folder.Path, "report.json"));
        var error = File.ReadAllBytes(Path.Combine(folder.Path, "error.txt"));
        Assert.Contains("The table dock.причал has no column нет.", Utf8(report), StringComparison.Ordinal);
        Assert.Contains("The table dock.причал has no column нет.", Utf8(error), StringComparison.Ordinal);
        Assert.False(report.AsSpan().StartsWith(Encoding.UTF8.Preamble), "the report begins with a byte order mark");
        Assert.Equal("437", File.ReadAllText(Path.Combine(folder.Path, "codepage.txt")).Trim());
    }

    /// <summary>The bytes as UTF-8, failing on any that aren't.</summary>
    private static string Utf8(byte[] bytes) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
}
