namespace Haruspex.Cli;

/// <summary>A file an emitter gives, written beside the IR: its name in the output folder, and its text.</summary>
/// <param name="Name">The file's name, such as <c>haruspex.zod.ts</c>.</param>
/// <param name="Text">What it holds.</param>
internal sealed record EmittedFile(string Name, string Text);
