namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// A database reference as a package's header records it: the package it names, where that package was when this one was
/// built, and which database it is.
/// </summary>
/// <param name="FileName">The path recorded when the package was built: absolute on the machine that built it, or bare for a system package such as <c>master.dacpac</c>.</param>
/// <param name="LogicalName">The package's file name, as the reference names it.</param>
/// <param name="Database">
/// Null for the same database (one database split across projects); otherwise the other database as the reference names it,
/// without its brackets: a name (<c>master</c>) or a SQLCMD variable (<c>$(Ledger)</c>).
/// </param>
internal sealed record PackageReference(string FileName, string LogicalName, string? Database);
