namespace Haruspex.SqlServer.Model;

/// <summary>
/// A part of the database the source refers to but which wasn't read, such as a referenced DACPAC that wasn't found: a name
/// that binds to nothing may be defined in it.
/// </summary>
/// <param name="Name">The part, as the source names it (<c>quay.dacpac</c>).</param>
/// <param name="ReferredToBy">What refers to it, as the source names that (<c>harbour.dacpac</c>).</param>
public sealed record UnreadPart(string Name, string ReferredToBy);
