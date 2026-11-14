namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>A package the one read refers to, directly or through another, and what became of it.</summary>
/// <param name="Name">Its file name, as the reference names it.</param>
/// <param name="Database">
/// Null when it is part of the same database, whose objects are read into the model; otherwise the other database, as the
/// reference names it (<c>master</c>, <c>$(Ledger)</c>), which is found and hashed but not read: names in another database are not
/// bound yet.
/// </param>
/// <param name="Found">The package found for it, or null when none was (then a warning says where it was looked for).</param>
public sealed record ReferenceRead(string Name, string? Database, LoadedPackage? Found);
