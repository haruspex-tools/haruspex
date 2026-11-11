using System.Globalization;
using System.Text;

namespace Haruspex.Tests.Snapshotting;

/// <summary>
/// Compares text with a committed <c>name.verified.ext</c> file. See README.md in this folder for why this
/// exists instead of a snapshot library.
/// </summary>
/// <param name="directory">The folder holding the snapshots.</param>
/// <param name="isCi">In CI nothing is ever written: no received file, and no accepting.</param>
/// <param name="accept">Overwrite the verified file with the actual text instead of failing.</param>
internal sealed class SnapshotStore(string directory, bool isCi, bool accept)
{
    public const string AcceptVariable = "HARUSPEX_ACCEPT";

    private const int ContextLines = 3;
    private const int MaxDiffLines = 80;

    public static SnapshotStore FromEnvironment(string directory) => new(
        directory,
        IsCiEnvironment(Environment.GetEnvironmentVariable),
        Environment.GetEnvironmentVariable(AcceptVariable) == "1");

    public static bool IsCiEnvironment(Func<string, string?> variable) =>
        !string.IsNullOrEmpty(variable("CI")) || !string.IsNullOrEmpty(variable("GITHUB_ACTIONS"));

    public SnapshotOutcome Check(string name, string extension, string actual)
    {
        var verifiedName = $"{name}.verified.{extension}";
        var receivedName = $"{name}.received.{extension}";
        var verifiedPath = Path.Combine(directory, verifiedName);
        var receivedPath = Path.Combine(directory, receivedName);

        // Line endings are normalised so a checkout with CRLF (git autocrlf on Windows) still matches.
        actual = Lf(actual);
        var verified = File.Exists(verifiedPath) ? Lf(File.ReadAllText(verifiedPath)) : null;

        if (verified == actual)
        {
            if (!isCi)
                File.Delete(receivedPath);
            return new SnapshotOutcome(true, $"{verifiedName} matches.");
        }

        if (accept && !isCi)
        {
            // File.WriteAllText writes UTF-8 without a byte order mark, as haruspex does.
            File.WriteAllText(verifiedPath, actual);
            File.Delete(receivedPath);
            return new SnapshotOutcome(true, $"Accepted {verifiedName}.");
        }

        if (!isCi)
            File.WriteAllText(receivedPath, actual);

        var message = new StringBuilder();
        message.Append(verified is null
            ? $"No verified snapshot {verifiedName} exists."
            : $"The text differs from {verifiedName}.");
        message.Append(isCi
            ? " (CI: nothing was written.)\n"
            : $" Wrote {receivedName}. Review it, then rename it to {verifiedName} or rerun with {AcceptVariable}=1.\n");
        if (verified is not null)
            message.Append(UnifiedDiff(verified, actual, verifiedName, receivedName));
        return new SnapshotOutcome(false, message.ToString());
    }

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>A unified diff with three lines of context, from a longest-common-subsequence of lines.</summary>
    internal static string UnifiedDiff(string expected, string actual, string expectedName, string actualName)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');

        // Snapshots are small, so the quadratic table is fine; huge ones fall back to the whole text.
        if ((long)a.Length * b.Length > 4_000_000)
            return $"--- {expectedName}\n+++ {actualName}\n(too large to diff: {a.Length} and {b.Length} lines)\n";

#pragma warning disable CA1814 // The longest-common-subsequence table is rectangular; a jagged array would only add allocations.
        var lcs = new int[a.Length + 1, b.Length + 1];
#pragma warning restore CA1814
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        var ops = new List<(char Kind, string Line)>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                ops.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(('-', a[x++]));
            }
            else
            {
                ops.Add(('+', b[y++]));
            }
        }

        while (x < a.Length)
            ops.Add(('-', a[x++]));
        while (y < b.Length)
            ops.Add(('+', b[y++]));

        var output = new StringBuilder($"--- {expectedName}\n+++ {actualName}\n");
        var written = 0;
        var index = 0;
        while (index < ops.Count)
        {
            var change = ops.FindIndex(index, o => o.Kind != ' ');
            if (change < 0)
                break;

            // Extend the hunk while the next change is close enough for the contexts to touch.
            var start = Math.Max(change - ContextLines, index);
            var end = change;
            for (var k = change + 1; k < ops.Count && k <= end + (2 * ContextLines); k++)
            {
                if (ops[k].Kind != ' ')
                    end = k;
            }

            end = Math.Min(end + ContextLines, ops.Count - 1);

            var aStart = 1 + ops.Take(start).Count(o => o.Kind != '+');
            var bStart = 1 + ops.Take(start).Count(o => o.Kind != '-');
            var hunk = ops.GetRange(start, end - start + 1);
            var aLength = hunk.Count(o => o.Kind != '+');
            var bLength = hunk.Count(o => o.Kind != '-');
            output.Append(CultureInfo.InvariantCulture, $"@@ -{(aLength == 0 ? aStart - 1 : aStart)},{aLength} +{(bLength == 0 ? bStart - 1 : bStart)},{bLength} @@\n");
            foreach (var (kind, line) in hunk)
            {
                if (written++ == MaxDiffLines)
                {
                    output.Append("... (diff truncated)\n");
                    return output.ToString();
                }

                output.Append(kind).Append(line).Append('\n');
            }

            index = end + 1;
        }

        return output.ToString();
    }
}
