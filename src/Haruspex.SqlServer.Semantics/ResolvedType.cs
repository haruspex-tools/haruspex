using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>A type with any alias resolved to the built-in type it stands for.</summary>
/// <param name="BuiltIn">The built-in type, such as <c>nvarchar(20)</c>.</param>
/// <param name="Alias">The alias type it was declared with (<c>sysname</c> included), or null.</param>
/// <param name="AliasNullability">What the alias type says about null; <see cref="DeclaredNullability.NotDeclared"/> without one.</param>
public sealed record ResolvedType(SqlType BuiltIn, string? Alias, DeclaredNullability AliasNullability)
{
    /// <summary>The name facts from the type are attributed to: the alias's, or the built-in type as written.</summary>
    public string SourceName => Alias ?? BuiltIn.ToString();
}
