namespace Haruspex.SqlServer.Semantics;

/// <summary>A fact implied by a type, such as <c>tinyint</c>'s range.</summary>
/// <param name="Type">The type's name: an alias's, or the built-in type as written.</param>
public sealed record TypeSource(string Type) : FactSource;
