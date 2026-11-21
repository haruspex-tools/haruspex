using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// A column the config file declares to hold JSON of a schema (0.28): a fact of the column that completes its type, which inference
/// reads at every read of the column. Recorded so that a reader can tell what was declared from what was inferred, and so that a
/// change to a schema file is a change to the document. Why: docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md.
/// </summary>
/// <param name="Column">The column, as <c>schema.table.column</c>, each part as the source names it.</param>
/// <param name="Schema">
/// The schema file, relative to the folder of the config file that declares it and <c>/</c>-separated, so the same on every machine.
/// </param>
/// <param name="Pointer">The JSON Pointer into the file of the schema used (<c>/$defs/person</c>), or null for the whole file.</param>
/// <param name="Files">
/// The SHA-256 of every schema file the declaration read, the file itself and every file a <c>$ref</c> reached, as 64 lower-case
/// hexadecimal digits, by the file's path, written as <paramref name="Schema"/> is.
/// </param>
public sealed record IrColumnDeclaration(string Column, string Schema, string? Pointer, EquatableMap<string, string> Files);
