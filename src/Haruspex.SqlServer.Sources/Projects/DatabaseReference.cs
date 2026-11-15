using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Sources.Projects;

/// <summary>
/// A reference a project makes to more of a database's schema: a project or a DACPAC, of the project's own database or another,
/// resolved to where its build takes it from (<see cref="ProjectReferences"/>).
/// </summary>
/// <param name="Kind">What it names.</param>
/// <param name="Described">What it names, in words, to complete "refers to": "the DACPAC ../lib/barrel.dacpac".</param>
/// <param name="FullPath">Where the build takes it from; null when that isn't known here (Visual Studio's own packages).</param>
/// <param name="FileName">The DACPAC's file name, by which one is looked for in the config file's <c>references</c> folders.</param>
/// <param name="Database">
/// Null for the project's own database; otherwise the other database as a body writes it: a name (<c>almanac</c>) or a SQLCMD
/// variable (<c>$(VaultDb)</c>).
/// </param>
/// <param name="At">The element that makes it.</param>
internal sealed record DatabaseReference(DatabaseReferenceKind Kind, string Described, string? FullPath, string FileName, string? Database, SourceLocation At);
