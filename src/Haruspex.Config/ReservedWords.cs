namespace Haruspex.Config;

/// <summary>
/// The words the language of the generated code keeps for itself, which a name settled for it must not be: TypeScript's,
/// for the Zod emitter. Which words those are is the emitter's knowledge, not the config file's, so the CLI hands them in
/// when it reads the config, as it hands the emitter its settings (<c>docs/architecture.md</c>).
/// </summary>
/// <param name="Language">The language they belong to, for messages: <c>TypeScript</c>.</param>
/// <param name="Contains">Whether a name is one of them.</param>
/// <remarks>
/// Checked here as well as by the emitter, so that a reserved name is reported where it comes from: at the line of the config
/// file that gives it, or as the naming rule that produced it. With <see cref="None"/>, the emitter still refuses one.
/// </remarks>
public sealed record ReservedWords(string Language, Func<string, bool> Contains)
{
    /// <summary>No target named: no word is reserved. The emitter is then the only check.</summary>
    public static ReservedWords None { get; } = new("no language", _ => false);
}
