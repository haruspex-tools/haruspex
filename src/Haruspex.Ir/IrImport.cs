namespace Haruspex.Ir;

/// <summary>
/// A value declared by the config file to be of a type the application already defines: a schema named
/// <paramref name="Name"/>, exported by the module <paramref name="From"/>. The emitter imports and uses it as it is,
/// so the value is still checked at runtime, by the application's own schema.
/// </summary>
/// <param name="Name">The exported name, an identifier (<see cref="IrIdentifiers"/>), such as <c>OrderSettingsSchema</c>.</param>
/// <param name="From">
/// The module that exports it, in the format's own terms: a path relative to the folder the IR file is written in, when it
/// starts <c>./</c> or <c>../</c> (written with <c>/</c>); otherwise a name the target resolves itself, such as a package or a
/// path alias (<c>@app/types</c>). What a module is, and how an import of one is written, is each emitter's to say.
/// </param>
public sealed record IrImport(string Name, string From) : IrType;
