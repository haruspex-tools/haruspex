namespace Haruspex.Cli;

internal static class Program
{
    private static int Main(string[] args) => HaruspexCli.Run(args, Console.Out, Console.Error);
}
