using System.CommandLine;
using System.CommandLine.Parsing;

namespace Haruspex.Cli;

/// <summary>
/// The options on a command line that its command doesn't have, each named as one with the nearest it does have, rather than read
/// as the source, the object or the field.
/// </summary>
/// <remarks>
/// Every command has an optional positional argument (<c>generate</c>'s and <c>check</c>'s source, <c>init</c>'s, and
/// <c>explain</c>'s field), and the parser gives such an argument any token it can't match to an option. So <c>check
/// --verse</c> was read as a source called <c>--verse</c> and answered with "directory not found", which sent the person
/// looking for the wrong thing. A token is an option haruspex doesn't know when it starts with <c>-</c>, comes before any
/// <c>--</c> (after which every token is an argument, as on every command line), and is no option's value (<c>--out -x</c> writes
/// to a folder called <c>-x</c>). A lone <c>-</c> is left as an argument, as it conventionally names a standard stream.
/// </remarks>
internal static class UnknownOptions
{
    /// <summary>One message for each option on the command line that its command doesn't have, in the order written.</summary>
    public static List<string> In(ParseResult parsed)
    {
        var command = parsed.CommandResult.Command;
        var values = parsed.CommandResult.Children.OfType<OptionResult>().SelectMany(o => o.Tokens).Select(t => t.Value).ToHashSet(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var token in parsed.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
                break;
            if (token.Type != TokenType.Argument || token.Value.Length < 2 || token.Value[0] != '-' || values.Contains(token.Value))
                continue;
            var nearest = Nearest(token.Value, command);
            unknown.Add($"{token.Value} is not an option of {Name(command)}{(nearest is null ? "" : $"; the nearest is {nearest}")}.");
        }

        return unknown;
    }

    /// <summary>The command as it is typed, <c>haruspex check</c>.</summary>
    private static string Name(Command command) => command is RootCommand ? "haruspex" : $"haruspex {command.Name}";

    /// <summary>
    /// The command's option whose name is fewest edits from <paramref name="written"/>'s (its value after <c>=</c> left out), and at
    /// most two: a British spelling is one from the American, and <c>--verse</c> two from <c>--verbose</c>. None when none is that close,
    /// since a guess further off would mislead more than it helps.
    /// </summary>
    private static string? Nearest(string written, Command command)
    {
        var name = written.Split('=', 2)[0];
        return command.Options
            .Where(o => !o.Hidden)
            .SelectMany(o => new[] { o.Name }.Concat(o.Aliases))
            .Where(n => n.StartsWith("--", StringComparison.Ordinal))
            .Select(n => (Name: n, Distance: Distance(name, n)))
            .Where(c => c.Distance <= 2)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => c.Name)
            .FirstOrDefault();
    }

    /// <summary>The Levenshtein distance: the fewest insertions, deletions and substitutions that turn one into the other.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
