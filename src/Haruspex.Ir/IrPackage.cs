namespace Haruspex.Ir;

/// <summary>A package a source was read from, as a file: what it is called and exactly which bytes it held (0.19).</summary>
/// <param name="Name">Its file name, without a folder, since a folder is where it was on one machine.</param>
/// <param name="Sha256">The SHA-256 of its bytes, as 64 lower-case hexadecimal digits.</param>
public sealed record IrPackage(string Name, string Sha256);
