using Haruspex.Cli.Output;
using Haruspex.Ir;

namespace Haruspex.Cli;

/// <summary>
/// The end of a message from <c>check</c> about a field whose claim changed: the <c>haruspex explain</c> command that says why it
/// is so, for each side whose IR there is, so that a refused change leads straight to its reason
/// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>). <c>check</c> writes no IR, so the module's side
/// is explained once <c>generate</c> has written it; a baseline given as an IR file is explained from that file.
/// </summary>
internal static class ExplainPointer
{
    /// <summary>
    /// The sentence that points at the module's field once generated, after a change as <c>check</c> words it (with no full stop),
    /// or nothing when the change isn't a field's.
    /// </summary>
    public static string Of(IrDocument document, IrChange change) =>
        IrFieldPlaces.Command(document, change.Object, change.Path) is { } command ? $". Why, once generated: {command}" : "";

    /// <summary>
    /// The sentence that points at the field in each database: the module's once generated, and the baseline's from its IR file
    /// when it was given as one.
    /// </summary>
    public static string Of(EmittedModule module, IrChange change)
    {
        var candidate = IrFieldPlaces.Command(module.Database, change.Object, change.Path);
        var baseline = module.Baseline is { IsIr: true } given && IrFieldPlaces.Command(given.Document, change.Object, change.Path) is { } command
            ? $"{command} --ir {DisplayPath.From(module.Config.Directory, given.Path)}"
            : null;
        return (candidate, baseline) switch
        {
            ({ } now, { } before) => $" Why: {now}, once generated, and in the baseline {before}.",
            ({ } now, null) => $" Why, once generated: {now}.",
            (null, { } before) => $" Why, in the baseline: {before}.",
            _ => "",
        };
    }
}
