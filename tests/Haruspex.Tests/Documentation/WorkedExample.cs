using System.Globalization;

namespace Haruspex.Tests.Documentation;

/// <summary>
/// A worked example in the documentation: a database project written as one SQL block, and what haruspex produces for it,
/// so that <see cref="WorkedExampleTests"/> can run the one and compare the other. The convention is in
/// <c>docs/internals/README.md</c>, "How the guide is kept true".
/// </summary>
/// <remarks>
/// An example is a fenced block whose info string is <c>sql haruspex-example</c>. The blocks after it, up to the next
/// example, may show its output: <c>ts haruspex-zod</c> for the Zod module, and <c>json haruspex-ir</c> for the IR, the
/// whole document or, with a third word, one of its top-level properties (<c>json haruspex-ir unknowns</c>), and
/// <c>json haruspex-jsonschema</c> for the JSON Schema document, whole or, with a third word, one of its definitions
/// (<c>json haruspex-jsonschema GetOrder</c>), and <c>ts haruspex-mssql</c> for the end of the database module's functions,
/// from the line the block starts with (<see cref="MssqlShown"/>). They may also
/// show a run of <c>haruspex generate</c>: <c>text haruspex-run</c> for what it prints, with the run's arguments after the
/// mark if any, and <c>jsonc haruspex-config</c> for the config file of the runs after it, up to the next. And a run of
/// <c>haruspex check</c>, <c>text haruspex-check</c>: what it prints when the example before is generated and committed, and
/// the project then changed to this one.
/// <para>
/// An example may name one of the vendored corpora after its mark (<c>sql haruspex-example WideWorldImporters</c>): its SQL is
/// then read beside that corpus's scripts, so that it can use the corpus's own tables, and only the objects it defines are
/// inferred. That is how the project's README shows haruspex on real tables, kept true by the same test.
/// </para>
/// </remarks>
/// <param name="File">The Markdown file, relative to the repository root.</param>
/// <param name="Line">The line of the example's opening fence.</param>
/// <param name="Sql">The project: tables, functions and procedures, separated by <c>GO</c>.</param>
/// <param name="Outputs">What the documentation says haruspex produces for it.</param>
internal sealed record WorkedExample(string File, int Line, string Sql, IReadOnlyList<WorkedExample.Output> Outputs)
{
    /// <summary>The vendored corpus the example is read beside, by its name, or null for an example that stands alone.</summary>
    public string? Over { get; init; }

    public const string ExampleMark = "haruspex-example";
    public const string ZodMark = "haruspex-zod";
    public const string IrMark = "haruspex-ir";
    public const string JsonSchemaMark = "haruspex-jsonschema";
    public const string MssqlMark = "haruspex-mssql";
    public const string ConfigMark = "haruspex-config";
    public const string RunMark = "haruspex-run";
    public const string CheckMark = "haruspex-check";

    /// <summary>The examples in a Markdown file, and what is wrong with how any is written.</summary>
    public static (IReadOnlyList<WorkedExample> Examples, IReadOnlyList<string> Problems) Read(string file, string markdown)
    {
        var examples = new List<WorkedExample>();
        var problems = new List<string>();
        WorkedExample? current = null;
        string? previous = null;
        string? config = null;
        foreach (var block in MarkdownText.FencedBlocks(markdown))
        {
            var mark = block.Words.Skip(1).FirstOrDefault();
            var at = $"{file}:{block.Line.ToString(CultureInfo.InvariantCulture)}";
            switch (mark)
            {
                case ExampleMark:
                    Close(current);
                    previous = current?.Sql;
                    current = new WorkedExample(file, block.Line, block.Text, []) { Over = block.Words.Skip(2).FirstOrDefault() };
                    config = null;
                    break;
                case ZodMark or IrMark or JsonSchemaMark or MssqlMark or ConfigMark or RunMark or CheckMark when current is null:
                    problems.Add($"{at}: a {mark} block with no {ExampleMark} block before it");
                    break;
                case ZodMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Zod, null, block.Line, block.Text)] };
                    break;
                case IrMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Ir, block.Words.Skip(2).FirstOrDefault(), block.Line, block.Text)] };
                    break;
                case JsonSchemaMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.JsonSchema, block.Words.Skip(2).FirstOrDefault(), block.Line, block.Text)] };
                    break;
                case MssqlMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Mssql, null, block.Line, block.Text)] };
                    break;
                case ConfigMark:
                    config = block.Text;
                    break;
                case RunMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Run, null, block.Line, block.Text) { Config = config, Arguments = [.. block.Words.Skip(2)] }] };
                    break;
                case CheckMark when previous is null:
                    problems.Add($"{at}: a {mark} block in the first {ExampleMark} block's outputs, with no project before it to have generated");
                    break;
                case CheckMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Check, null, block.Line, block.Text) { Config = config, Arguments = [.. block.Words.Skip(2)], Before = previous }] };
                    break;
                default:
                    break;
            }
        }

        Close(current);
        return (examples, problems);

        void Close(WorkedExample? example)
        {
            if (example is null)
                return;
            if (example.Outputs.Count == 0)
                problems.Add($"{example.File}:{example.Line.ToString(CultureInfo.InvariantCulture)}: a {ExampleMark} block with no output block after it");
            else
                examples.Add(example);
        }
    }

    /// <summary>
    /// The module as a Zod block shows it: whole, when the block starts with the header comment, and otherwise from the line
    /// after <c>import</c>, so that an example need not repeat the header (which names the IR's version).
    /// </summary>
    public static string ZodShown(string module, string shown)
    {
        if (shown.StartsWith("//", StringComparison.Ordinal))
            return module;
        var lines = module.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var import = lines.FindIndex(l => l.StartsWith("import ", StringComparison.Ordinal));
        return string.Join('\n', lines.Skip(import + 1).SkipWhile(l => l.Length == 0));
    }

    /// <summary>
    /// The functions' module as a block of it shows it: from the line the block starts with to the end, where the functions
    /// are, so that an example shows a procedure's function without the error classes and helpers every module begins with.
    /// The whole module when the block's first line isn't in it, so that the comparison fails and shows what is.
    /// </summary>
    public static string MssqlShown(string module, string shown)
    {
        var lines = module.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var first = shown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];
        var start = lines.IndexOf(first);
        return start < 0 ? module : string.Join('\n', lines.Skip(start));
    }

    /// <summary>What the IR, the Zod or the JSON Schema should be, and where the documentation says so.</summary>
    /// <param name="Kind">Which output.</param>
    /// <param name="Property">
    /// For the IR, the top-level property shown, and for the JSON Schema, the definition shown; null for the whole document.
    /// </param>
    /// <param name="Line">The line of the block's opening fence.</param>
    /// <param name="Text">The block's text.</param>
    public sealed record Output(OutputKind Kind, string? Property, int Line, string Text)
    {
        /// <summary>For a run, the config file it reads, or null for none.</summary>
        public string? Config { get; init; }

        /// <summary>For a run, its arguments after <c>generate</c> or <c>check</c>; none means the config file's settings, or every procedure to <c>out</c> without one.</summary>
        public IReadOnlyList<string> Arguments { get; init; } = [];

        /// <summary>For a check, the project of the example before, generated first, so that its output is what is committed.</summary>
        public string? Before { get; init; }
    }

    /// <summary>The outputs an example can show.</summary>
    public enum OutputKind
    {
        Ir,
        Zod,
        JsonSchema,
        Mssql,
        Run,
        Check,
    }
}
