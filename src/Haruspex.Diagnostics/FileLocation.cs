namespace Haruspex.Diagnostics;

/// <summary>
/// A file or folder as a whole, for a problem that has no line: a config file whose generated names clash, a folder that
/// can't be written to.
/// </summary>
/// <param name="File">The file or folder, as it was found or named.</param>
public sealed record FileLocation(string File) : DiagnosticLocation;
