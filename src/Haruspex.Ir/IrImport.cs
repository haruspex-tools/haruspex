namespace Haruspex.Ir;

/// <summary>
/// A value declared by the config file to be of a type the application already defines: a schema named
/// <paramref name="Name"/>, exported by the module <paramref name="From"/>. The emitter imports and uses it as it is,
/// so the value is still checked at runtime, by the application's own schema.
/// </summary>
/// <param name="Name">The exported name, an identifier (<see cref="IrIdentifiers"/>), such as <c>OrderSettingsSchema</c>.</param>
/// <param name="From">The module to import it from, as the generated module should write it.</param>
public sealed record IrImport(string Name, string From) : IrType;
