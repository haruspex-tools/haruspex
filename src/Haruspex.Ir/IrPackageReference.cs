namespace Haruspex.Ir;

/// <summary>
/// A package the source's package refers to (0.19): the same database split across projects, or another database. Recorded
/// whether or not it was found, so that a file says what its claims rest on and what they lack.
/// </summary>
/// <param name="Name">Its file name, as the referring package names it.</param>
/// <param name="Sha256">The SHA-256 of the package read for it; null when none was found, and then nothing in it was read.</param>
/// <param name="Database">
/// Null when it is part of the same database; otherwise the other database, as the reference names it: a name
/// (<c>master</c>) or a SQLCMD variable (<c>$(Ledger)</c>), whose value is not known until it is deployed.
/// </param>
public sealed record IrPackageReference(string Name, string? Sha256, string? Database);
