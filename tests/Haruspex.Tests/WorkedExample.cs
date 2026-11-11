using System.Globalization;

namespace Haruspex.Tests;

/// <summary>
/// A worked example in the documentation: a database project written as one SQL block, and what haruspex produces for it,
/// so that <see cref="WorkedExampleTests"/> can run the one and compare the other. The convention is in
/// <c>docs/internals/README.md</c>, "How the guide is kept true".
/// </summary>
/// <remarks>
/// An example is a fenced block whose info string is <c>sql haruspex-example</c>. The blocks after it, up to the next
/// example, may show its output: <c>ts haruspex-zod</c> for the Zod module, and <c>json haruspex-ir</c> for the IR, the
/// whole document or, with a third word, one of its top-level properties (<c>json haruspex-ir unknowns</c>).
/// </remarks>
/// <param name="File">The Markdown file, relative to the repository root.</param>
/// <param name="Line">The line of the example's opening fence.</param>
/// <param name="Sql">The project: tables, functions and procedures, separated by <c>GO</c>.</param>
/// <param name="Outputs">What the documentation says haruspex produces for it.</param>
internal sealed record WorkedExample(string File, int Line, string Sql, IReadOnlyList<WorkedExample.Output> Outputs)
{
    public const string ExampleMark = "haruspex-example";
    public const string ZodMark = "haruspex-zod";
    public const string IrMark = "haruspex-ir";

    /// <summary>The examples in a Markdown file, and what is wrong with how any is written.</summary>
    public static (IReadOnlyList<WorkedExample> Examples, IReadOnlyList<string> Problems) Read(string file, string markdown)
    {
        var examples = new List<WorkedExample>();
        var problems = new List<string>();
        WorkedExample? current = null;
        foreach (var block in MarkdownText.FencedBlocks(markdown))
        {
            var mark = block.Words.Skip(1).FirstOrDefault();
            var at = $"{file}:{block.Line.ToString(CultureInfo.InvariantCulture)}";
            switch (mark)
            {
                case ExampleMark:
                    Close(current);
                    current = new WorkedExample(file, block.Line, block.Text, []);
                    break;
                case ZodMark or IrMark when current is null:
                    problems.Add($"{at}: a {mark} block with no {ExampleMark} block before it");
                    break;
                case ZodMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Zod, null, block.Line, block.Text)] };
                    break;
                case IrMark:
                    current = current with { Outputs = [.. current.Outputs, new Output(OutputKind.Ir, block.Words.Skip(2).FirstOrDefault(), block.Line, block.Text)] };
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

    /// <summary>What the IR or the Zod should be, and where the documentation says so.</summary>
    /// <param name="Kind">Which output.</param>
    /// <param name="Property">For the IR, the top-level property shown, or null for the whole document.</param>
    /// <param name="Line">The line of the block's opening fence.</param>
    /// <param name="Text">The block's text.</param>
    public sealed record Output(OutputKind Kind, string? Property, int Line, string Text);

    /// <summary>The outputs an example can show.</summary>
    public enum OutputKind
    {
        Ir,
        Zod,
    }
}
