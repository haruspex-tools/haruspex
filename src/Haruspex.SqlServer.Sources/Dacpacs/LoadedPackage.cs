namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>A package that was read, as its file: its name, where it was, and exactly which bytes it held.</summary>
/// <param name="Name">Its file name.</param>
/// <param name="Path">Where it was found.</param>
/// <param name="Sha256">The SHA-256 of its bytes, in lower-case hexadecimal.</param>
public sealed record LoadedPackage(string Name, string Path, string Sha256);
