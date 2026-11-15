namespace Haruspex.Tests.Coverage;

/// <summary>
/// What haruspex does with each construct ScriptDom parses: <c>tests/coverage/syntax.txt</c>, an entry for every node type and
/// operator <see cref="ScriptDomNodes"/> lists, in the sections of the coverage page. The file's head and
/// <c>tests/coverage/README.md</c> give its format; <c>CoveragePageTests</c> runs each entry's example to check its claim.
/// </summary>
/// <param name="Sections">The page's sections, in the file's order.</param>
internal sealed record SyntaxInventory(IReadOnlyList<SyntaxInventory.Section> Sections)
{
    /// <summary>The file, from the repository's root.</summary>
    public const string Path = "tests/coverage/syntax.txt";

    /// <summary>The inventory as the file holds it.</summary>
    public static SyntaxInventory Current { get; } = Read(File.ReadAllLines(System.IO.Path.Combine(RepositoryPaths.Root, Path)));

    /// <summary>Every entry, in the file's order.</summary>
    public IEnumerable<Entry> Entries => Sections.SelectMany(s => s.Entries);

    /// <summary>Reads the file: a <c>[heading]</c> starts a section, and each other line that isn't a comment is an entry.</summary>
    public static SyntaxInventory Read(IEnumerable<string> lines)
    {
        var sections = new List<(string Title, List<Entry> Entries)>();
        foreach (var (line, number) in lines.Select((l, i) => (l.Trim(), i + 1)).Where(l => l.Item1.Length > 0 && !l.Item1.StartsWith('#')))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
                sections.Add((line[1..^1], []));
            else if (sections.Count == 0)
                throw new FormatException($"{Path}:{number}: an entry before any section.");
            else
                sections[^1].Entries.Add(Parse(line, number));
        }

        return new SyntaxInventory([.. sections.Select(s => new Section(s.Title, [.. s.Entries]))]);
    }

    /// <summary>
    /// One line: a bare name is a statement the walker has no case for; otherwise six fields split on bars, where <c>\|</c>
    /// writes a bar inside one.
    /// </summary>
    private static Entry Parse(string line, int number)
    {
        const string Bar = "\u0001";
        var fields = line.Replace(@"\|", Bar, StringComparison.Ordinal).Split('|').Select(f => f.Trim().Replace(Bar, "|", StringComparison.Ordinal)).ToArray();
        if (fields.Length == 1)
            return new Entry(fields[0], "", ClaimKind.Unlisted, null, null, "", []);
        if (fields.Length != 6)
            throw new FormatException($"{Path}:{number}: {fields.Length} fields where an entry has 6.");
        var claim = fields[2].Split(' ', 2);
        var kind = claim[0] switch
        {
            "modelled" => ClaimKind.Modelled,
            "no-effect" => ClaimKind.NoEffect,
            "unknown" => ClaimKind.Unknown,
            "partly" => ClaimKind.Partly,
            "not-in-a-body" => ClaimKind.NotInABody,
            "not-parsed" => ClaimKind.NotParsed,
            "within" => ClaimKind.Within,
            _ => throw new FormatException($"{Path}:{number}: no claim {claim[0]}."),
        };
        var argument = claim.Length == 2 ? claim[1] : null;
        if (kind is ClaimKind.Unknown or ClaimKind.Partly or ClaimKind.Within != (argument is not null))
            throw new FormatException($"{Path}:{number}: {claim[0]} {(argument is null ? "needs" : "takes no")} argument.");
        string[] examples = fields[5].Length == 0 ? [] : [.. fields[5].Split("&&").Select(e => e.Trim())];
        return new Entry(fields[0], fields[1], kind, argument, fields[3].Length == 0 ? null : fields[3], fields[4], examples);
    }

    /// <summary>A section of the page, under a heading of its own.</summary>
    /// <param name="Title">The heading.</param>
    /// <param name="Entries">Its entries, in the file's order.</param>
    public sealed record Section(string Title, IReadOnlyList<Entry> Entries);

    /// <summary>One construct and what haruspex does with it.</summary>
    /// <param name="Node">The ScriptDom type, or an enum member as <c>Type.Member</c>.</param>
    /// <param name="Syntax">The construct as T-SQL writes it.</param>
    /// <param name="Claim">What haruspex does with it.</param>
    /// <param name="Argument">The unknown's reason, as the IR writes it, for <see cref="ClaimKind.Unknown"/> and <see cref="ClaimKind.Partly"/>; the entry a <see cref="ClaimKind.Within"/> entry is part of.</param>
    /// <param name="Section">The heading of the inference's README that explains it.</param>
    /// <param name="What">How its result is typed and when it is null, or what haruspex says instead.</param>
    /// <param name="Examples">Procedure bodies that show the claim, as <see cref="ClaimKind"/> says.</param>
    public sealed record Entry(string Node, string Syntax, ClaimKind Claim, string? Argument, string? Section, string What, IReadOnlyList<string> Examples);

    /// <summary>What haruspex does with a construct, which its examples show.</summary>
    public enum ClaimKind
    {
        /// <summary>It reads it and types what it gives: the example gives no unknown.</summary>
        Modelled,

        /// <summary>It reads it, and it changes no result: the example gives no unknown.</summary>
        NoEffect,

        /// <summary>An unknown with the entry's reason: the example gives it.</summary>
        Unknown,

        /// <summary>Some forms are modelled (the first example gives no unknown) and others give the reason (the second gives it).</summary>
        Partly,

        /// <summary>The parser refuses it in a procedure's body: the example doesn't parse as one.</summary>
        NotInABody,

        /// <summary>The parser never makes it from a body: the example, if any, has no such node.</summary>
        NotParsed,

        /// <summary>A part of another construct, whose entry describes it.</summary>
        Within,

        /// <summary>A statement the walker has no case for, which ends what haruspex can list (unsupported-statement).</summary>
        Unlisted,
    }
}
