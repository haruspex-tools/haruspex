namespace Haruspex.Config;

/// <summary>
/// A JSON document a declared schema is read from: a schema file, or the config file itself for a schema written in place. A
/// <c>$ref</c> without a file refers into the document it is written in, and a path in a problem is relative to it.
/// </summary>
/// <param name="File">The file, as a full path, which problems are reported against.</param>
/// <param name="Root">The document's root: the whole file, or the schema written in place.</param>
/// <param name="RootPath">Where <paramref name="Root"/> is in <paramref name="File"/>, as problems write a place; empty for a whole file.</param>
internal sealed record SchemaDocument(string File, JsonValue Root, string RootPath);
