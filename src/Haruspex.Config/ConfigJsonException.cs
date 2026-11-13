using System.Diagnostics.CodeAnalysis;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>Text that is not JSON with comments, and where.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Thrown and caught inside Config, always with a position.")]
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Thrown and caught inside Config; never part of its API.")]
internal sealed class ConfigJsonException(DiagnosticCode code, string message, int line, int column, string path = "") : Exception(message)
{
    public DiagnosticCode Code { get; } = code;

    public string Path { get; } = path;

    public int Line { get; } = line;

    public int Column { get; } = column;
}
