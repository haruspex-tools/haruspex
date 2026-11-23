using System.Text;

namespace Haruspex.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        using var output = Console.IsOutputRedirected ? Utf8(Console.OpenStandardOutput()) : null;
        using var error = Console.IsErrorRedirected ? Utf8(Console.OpenStandardError()) : null;
        return HaruspexCli.Run(args, output ?? Console.Out, error ?? Console.Error);
    }

    /// <summary>
    /// A writer of UTF-8, with no byte order mark, over a standard stream sent to a file or a pipe, so that what a run writes there
    /// is the same bytes on every machine (<c>docs/command-line.md</c>, "Colour, links and progress").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Console.Out"/> and <see cref="Console.Error"/> encode in the console's encoding. On Windows that is the console's
    /// output code page (437, 850, 932 and so on), redirected or not, so a name outside it reached a JSON or SARIF report as
    /// <c>?</c> (observed on Windows under code page 437: a table named in Cyrillic written as <c>dock.??????</c>). Elsewhere
    /// it follows the locale, which is UTF-8 almost everywhere but not by rule.
    /// </para>
    /// <para>
    /// Setting <see cref="Console.OutputEncoding"/> to UTF-8 would also fix the bytes, but on Windows it changes the code page of
    /// the console haruspex shares with the shell that started it (<c>SetConsoleOutputCP</c>), and the change outlasts the run. So
    /// the console's writers are kept for a terminal, where a person reads the text and a character the code page lacks shows as it
    /// does for every program in that console, and a redirected stream, which a program reads, gets a writer of its own. It flushes
    /// each write, as the console's do, so that standard output and standard error sent to one file keep their order. Only the
    /// run's own thread writes to it: the progress line, the one thing drawn from another thread, is drawn only on a terminal.
    /// </para>
    /// </remarks>
    private static StreamWriter Utf8(Stream stream) =>
        new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
}
