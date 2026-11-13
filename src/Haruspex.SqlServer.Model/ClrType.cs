using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A type whose values come from .NET code (<c>CREATE TYPE … EXTERNAL NAME</c>). It is recorded so that an unqualified type
/// name that could mean it is not resolved to an alias type of the same name in another schema.
/// </summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its declaration is.</param>
public sealed record ClrType(ObjectName Name, SourceSpan Span) : UserType(Name, Span);
