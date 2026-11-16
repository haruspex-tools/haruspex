using Haruspex.Diagnostics;

namespace Haruspex.Emit.Mssql;

/// <summary>
/// The codes of what the mssql emitter refuses (HAR52xx), each explained on its page in <c>docs/diagnostics</c>: a document whose
/// functions it can't name, refused before anything is written.
/// </summary>
public static class MssqlCodes
{
    /// <summary>A procedure whose function's name is reserved, or is a name the module already binds.</summary>
    public static DiagnosticCode FunctionNameTaken { get; } = new(5201, DiagnosticSeverity.Error, "A procedure's function can't take its name");
}
