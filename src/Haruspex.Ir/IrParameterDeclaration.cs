using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A procedure's or function's parameter declared to hold JSON of a schema (0.37), by the config file's <c>parameters</c> or by
/// the extended property <c>haruspex.jsonSchema</c> on the parameter: a claim about every caller, which the body's reads of the
/// parameter take as given, and which the generated code checks before it sends a value. Recorded so that a reader can tell what
/// was declared from what was inferred, and so that a change to a schema file is a change to the document.
/// Why: docs/decisions/0143-a-parameter-s-json-may-be-declared.md.
/// </summary>
/// <param name="Parameter">
/// The parameter, as <c>schema.object.@name</c>, each part as the source names it, in brackets when it holds a dot or a bracket.
/// </param>
/// <param name="Schema">
/// The schema file, relative to the folder of the config file that declares it and <c>/</c>-separated, so the same on every machine.
/// </param>
/// <param name="Pointer">The JSON Pointer into the file of the schema used (<c>/$defs/order</c>), or null for the whole file.</param>
/// <param name="Files">
/// The SHA-256 of every schema file the declaration read, by path, as <see cref="IrColumnDeclaration.Files"/> gives them.
/// </param>
public sealed record IrParameterDeclaration(string Parameter, string Schema, string? Pointer, EquatableMap<string, string> Files);
