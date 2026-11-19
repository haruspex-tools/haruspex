using Haruspex.Ir;

namespace Haruspex.Emit.Zod;

/// <summary>
/// Settings for the Zod emitter. The emitter never reads the config file: whoever composes the tool (the CLI) maps the
/// config's settings to these.
/// </summary>
public sealed record ZodOptions
{
    /// <summary>How result objects treat keys their type does not list.</summary>
    public ZodObjects Objects { get; init; } = ZodObjects.Strip;

    /// <summary>
    /// For a module split into files, the specifier a module imports the shared file by: <c>./shared.zod.js</c>, the file the CLI
    /// writes beside the modules. Unused by a module of one file.
    /// </summary>
    public string SharedModule { get; init; } = "./shared.zod.js";

    /// <summary>
    /// Which fields a comment explains (<c>"comments"</c> in the config file): the surprises by default, every field, or none
    /// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>). The notes that state a fact a check rests on are
    /// written whatever this says.
    /// </summary>
    public IrComments Comments { get; init; } = IrComments.Surprises;
}
