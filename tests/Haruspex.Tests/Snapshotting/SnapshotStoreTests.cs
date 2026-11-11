namespace Haruspex.Tests.Snapshotting;

public class SnapshotStoreTests
{
    private static SnapshotStore Local(TempDirectory dir, bool accept = false) => new(dir.Path, isCi: false, accept);

    private static string Verified(TempDirectory dir) => Path.Combine(dir.Path, "T.m.verified.txt");

    private static string Received(TempDirectory dir) => Path.Combine(dir.Path, "T.m.received.txt");

    [Fact]
    public void Passes_when_the_text_matches_and_removes_a_stale_received_file()
    {
        using var dir = new TempDirectory();
        dir.File("T.m.verified.txt", "a\nb\n");
        dir.File("T.m.received.txt", "old\n");

        var outcome = Local(dir).Check("T.m", "txt", "a\nb\n");

        Assert.True(outcome.Passed, outcome.Message);
        Assert.False(File.Exists(Received(dir)));
    }

    [Fact]
    public void Compares_crlf_and_lf_as_equal()
    {
        using var dir = new TempDirectory();
        dir.File("T.m.verified.txt", "a\r\nb\r\n");

        Assert.True(Local(dir).Check("T.m", "txt", "a\nb\n").Passed);
    }

    [Fact]
    public void A_mismatch_writes_the_received_file_and_fails_with_a_diff()
    {
        using var dir = new TempDirectory();
        dir.File("T.m.verified.txt", "a\nold\nc\n");

        var outcome = Local(dir).Check("T.m", "txt", "a\nnew\nc\n");

        Assert.False(outcome.Passed);
        Assert.Equal("a\nnew\nc\n", File.ReadAllText(Received(dir)));
        Assert.Equal("a\nold\nc\n", File.ReadAllText(Verified(dir)));
        Assert.Contains("--- T.m.verified.txt\n+++ T.m.received.txt\n@@ -1,4 +1,4 @@\n a\n-old\n+new\n c\n", outcome.Message, StringComparison.Ordinal);
        Assert.Contains("HARUSPEX_ACCEPT=1", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_diff_shows_only_the_changed_lines_with_three_lines_of_context()
    {
        using var dir = new TempDirectory();
        var lines = Enumerable.Range(1, 20).Select(i => $"line {i}").ToList();
        dir.File("T.m.verified.txt", string.Join("\n", lines) + "\n");
        lines[9] = "changed";

        var outcome = Local(dir).Check("T.m", "txt", string.Join("\n", lines) + "\n");

        Assert.Contains("@@ -7,7 +7,7 @@\n line 7\n line 8\n line 9\n-line 10\n+changed\n line 11\n line 12\n line 13\n", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 6\n", outcome.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("line 14", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_verified_file_writes_the_received_file_and_fails()
    {
        using var dir = new TempDirectory();

        var outcome = Local(dir).Check("T.m", "txt", "new\n");

        Assert.False(outcome.Passed);
        Assert.Equal("new\n", File.ReadAllText(Received(dir)));
        Assert.False(File.Exists(Verified(dir)));
        Assert.Contains("T.m.verified.txt", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepting_overwrites_the_verified_file_with_lf_endings_and_passes()
    {
        using var dir = new TempDirectory();
        dir.File("T.m.verified.txt", "old\n");
        dir.File("T.m.received.txt", "stale\n");

        var outcome = Local(dir, accept: true).Check("T.m", "txt", "new\r\n");

        Assert.True(outcome.Passed, outcome.Message);
        Assert.Equal("new\n"u8.ToArray(), File.ReadAllBytes(Verified(dir)));
        Assert.False(File.Exists(Received(dir)));
    }

    [Fact]
    public void In_ci_it_never_writes_and_never_accepts()
    {
        using var dir = new TempDirectory();
        dir.File("T.m.verified.txt", "old\n");

        var outcome = new SnapshotStore(dir.Path, isCi: true, accept: true).Check("T.m", "txt", "new\n");

        Assert.False(outcome.Passed);
        Assert.Equal("old\n", File.ReadAllText(Verified(dir)));
        Assert.False(File.Exists(Received(dir)));
        Assert.Contains("+new", outcome.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", null, true)]
    [InlineData(null, "true", true)]
    [InlineData("", "", false)]
    public void Ci_is_recognised_from_the_environment(string? ci, string? githubActions, bool expected) =>
        Assert.Equal(expected, SnapshotStore.IsCiEnvironment(name => name switch
        {
            "CI" => ci,
            "GITHUB_ACTIONS" => githubActions,
            _ => null,
        }));
}
