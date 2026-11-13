using System.Text.RegularExpressions;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference;
using Haruspex.SqlServer.Semantics;

namespace Haruspex.Tests.Inference;

/// <summary>
/// The function table (<c>Typer.FunctionTable</c>) as data: a row for each function of the catalogue and no other;
/// each row's observation tests exist and call its function in its form; the reasons to be NULL the sweep finds for a call are
/// among those its row declares; and a function's name is read only through the catalogue, so the table is the one path a call
/// is typed by.
/// </summary>
public partial class FunctionTableTests
{
    /// <summary>Every test method of the differential tests' observation and grid classes, by name, with its body.</summary>
    private static readonly Dictionary<string, string> Observations = Directory
        .EnumerateFiles(Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.DifferentialTests"), "*.cs")
        .Where(f => f.EndsWith("ObservationTests.cs", StringComparison.Ordinal) || f.EndsWith("GridTests.cs", StringComparison.Ordinal))
        .SelectMany(f => Methods(File.ReadAllText(f)))
        .ToDictionary(m => m.Name, m => m.Body, StringComparer.Ordinal);

    private static readonly string[] SqlLayers = ["Haruspex.SqlServer.Inference", "Haruspex.SqlServer.Semantics"];

    private static IReadOnlyList<FunctionTyping> Table => Typer.FunctionTable;

    [Fact]
    public void Every_function_of_the_catalogue_has_one_row_and_every_row_a_function_of_it()
    {
        Assert.Equal(Table.Count, Table.Select(t => t.Function).Distinct().Count());
        Assert.Empty(BuiltInFunctions.All.Except(Table.Select(t => t.Function)).Select(f => f.Code));
        Assert.Empty(Table.Select(t => t.Function).Except(BuiltInFunctions.All).Select(f => f.Code));
    }

    [Fact]
    public void Every_row_cites_observation_tests_that_exist_and_call_its_function_in_its_form()
    {
        var problems = Table
            .SelectMany(t => t.ObservedIn.Count == 0 ? [$"{t.Function.Code}: cites no observation test"] : t.ObservedIn.Select(name =>
                !Observations.TryGetValue(name, out var body) ? $"{t.Function.Code}: no observation test {name}"
                : !Calls(body, t.Function) ? $"{t.Function.Code}: {name} doesn't call {t.Function.Name.ToUpperInvariant()} as {t.Function.Form.ToString().ToLowerInvariant()}"
                : null))
            .OfType<string>()
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Every_reason_the_sweep_finds_a_call_may_be_null_is_one_its_row_declares()
    {
        // The sweep's arguments are never-null columns and literals, so each reason a call gives is its own.
        var wire = Enum.GetValues<IrNullRule>().ToDictionary(IrWireNames.Of, r => r, StringComparer.Ordinal);
        var problems = new List<string>();
        var reached = new HashSet<BuiltInFunction>();
        foreach (var form in new[] { "function", "window", "aggregate", "cast", "isnull" })
        {
            var path = Path.Combine(RepositoryPaths.Root, "tests", "Haruspex.Tests", "Snapshots", $"CatalogueSweepTests.{form}.verified.txt");
            foreach (var line in File.ReadAllLines(path))
            {
                var arrow = line.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow < 0 || CalledIn(line[..arrow]) is not { } function)
                    continue;
                reached.Add(function);
                var row = Table.Single(t => t.Function == function);
                var because = Regex.Match(line[(arrow + 4)..], @"null:(?<rules>[a-z,-]+)").Groups["rules"].Value;
                problems.AddRange(because.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Where(r => !row.MayBeNullBecause.Contains(wire[r]))
                    .Select(r => $"{line[..arrow]}: {r}, which {function.Code} doesn't declare"));
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));

        // The sweep reaches every function the table types, but GROUPING and GROUPING_ID, which need a GROUP BY.
        Assert.Empty(Table.Where(t => t.Type is not null && !reached.Contains(t.Function))
            .Select(t => t.Function.Code).Except(["aggregate.grouping", "aggregate.grouping_id"]));
    }

    [Fact]
    public void A_functions_name_is_read_only_through_the_catalogue()
    {
        // One read that names nothing to type by: a user's function, called with its schema.
        const string allowed = "new ObjectName(schema.Value, call.FunctionName.Value)";
        var problems = SqlLayers
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root, "src", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l.Trim())))
            .Where(l => l.Text.Contains("FunctionName.Value", StringComparison.Ordinal)
                && !l.Text.Contains("BuiltInFunctions.Named(", StringComparison.Ordinal) && !l.Text.Contains("F.Named(", StringComparison.Ordinal)
                && !l.Text.Contains(allowed, StringComparison.Ordinal))
            .Select(l => $"{l.File}:{l.Line}: {l.Text}")
            .ToList();
        Assert.True(problems.Count == 0, "A function's name read outside the catalogue:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Whether a test's body calls the function in its form: a window function's name, its arguments, and <c>OVER</c> (after
    /// any <c>WITHIN GROUP</c>); any other's name and its arguments with no <c>OVER</c> after them; a keyword alone.
    /// </summary>
    private static bool Calls(string body, BuiltInFunction function)
    {
        if (function == ScalarFunctions.CurrentTimestamp || function == RowsetFunctions.OpenJson)
            return Regex.IsMatch(body, $@"\b{function.Name}\b", RegexOptions.IgnoreCase);
        return Regex.Matches(body, $@"\b{function.Name}\s*\(", RegexOptions.IgnoreCase)
            .Any(m => Windowed(body, m.Index + m.Length - 1) == (function.Form == FunctionForm.Window));
    }

    /// <summary>Whether the arguments opening at <paramref name="open"/> are followed by <c>OVER</c>, after any <c>WITHIN GROUP</c>.</summary>
    private static bool Windowed(string body, int open)
    {
        var rest = body[(Closing(body, open) + 1)..];
        var within = Regex.Match(rest, @"^\s*WITHIN\s+GROUP\s*\(", RegexOptions.IgnoreCase);
        if (within.Success)
            rest = rest[(Closing(rest, within.Length - 1) + 1)..];
        return Regex.IsMatch(rest, @"^\s*OVER\b", RegexOptions.IgnoreCase);
    }

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, or the text's last index.</summary>
    private static int Closing(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
                return i;
        }

        return text.Length - 1;
    }

    /// <summary>The function an expression of the sweep is one call of, or null when it is anything else.</summary>
    private static BuiltInFunction? CalledIn(string expression)
    {
        if (expression == "CURRENT_TIMESTAMP")
            return ScalarFunctions.CurrentTimestamp;
        var open = expression.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !Regex.IsMatch(expression[..open], "^[A-Z_0-9]+$"))
            return null;
        var depth = 0;
        var close = -1;
        for (var i = open; i < expression.Length && close < 0; i++)
        {
            depth += expression[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
                close = i;
        }

        var rest = expression[(close + 1)..];
        if (rest.Length > 0 && !rest.StartsWith(" OVER (", StringComparison.Ordinal) && !rest.StartsWith(" WITHIN GROUP (", StringComparison.Ordinal))
            return null;
        var name = expression[..open];
        return rest.Contains(" OVER (", StringComparison.Ordinal) ? BuiltInFunctions.Named(name, FunctionForm.Window)
            : BuiltInFunctions.Named(name, FunctionForm.Aggregate) ?? BuiltInFunctions.Named(name, FunctionForm.Scalar);
    }

    /// <summary>Each test method in a source file: its name, and its body up to the brace that closes it.</summary>
    private static IEnumerable<(string Name, string Body)> Methods(string source) =>
        TestMethod().Matches(source).Select(m =>
        {
            var end = source.IndexOf("\n    }\n", m.Index, StringComparison.Ordinal);
            return (m.Groups["name"].Value, source[m.Index..(end < 0 ? source.Length : end)]);
        });

    [GeneratedRegex(@"\[(?:Fact|Theory)[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public (?:async Task|void) (?<name>\w+)\(")]
    private static partial Regex TestMethod();
}
