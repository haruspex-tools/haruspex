using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A user-defined type a column can be declared with: an alias type (<c>CREATE TYPE … FROM</c>), a built-in type
/// under a name of its own; or a CLR type (<c>CREATE TYPE … EXTERNAL NAME</c>), whose values come from .NET code.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="BaseType">For an alias type, the type it stands for as written, such as <c>nvarchar(20)</c>; null for a CLR type.</param>
/// <param name="Nullability">
/// What an alias type's declaration says about null. A column of the type that says neither NULL nor NOT NULL
/// takes it. <see cref="DeclaredNullability.NotDeclared"/> for a CLR type.
/// </param>
/// <param name="At">Where it is declared.</param>
public sealed record UserType(ObjectName Name, SqlType? BaseType, DeclaredNullability Nullability, SourceLocation At)
{
    /// <summary>Whether it is an alias type, rather than a CLR type.</summary>
    public bool IsAlias => BaseType is not null;
}
