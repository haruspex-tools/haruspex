using Haruspex.Diagnostics;

namespace Haruspex.Emit.JsonSchema;

/// <summary>
/// The codes of what the JSON Schema emitter refuses (HAR51xx), each explained on its page in <c>docs/diagnostics</c>: a document
/// it can't write as one JSON Schema, refused before anything is built.
/// </summary>
public static class JsonSchemaCodes
{
    /// <summary>One name given to two definitions, a procedure and a type or two procedures, which one <c>$defs</c> can't hold.</summary>
    public static DiagnosticCode NamedTwice { get; } = new(5101, DiagnosticSeverity.Error, "One name for two definitions");
}
