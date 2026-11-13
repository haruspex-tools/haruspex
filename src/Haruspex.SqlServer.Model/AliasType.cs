using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>A built-in type under a name of its own (<c>CREATE TYPE sales.code FROM nvarchar(20) NOT NULL</c>).</summary>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where its declaration is.</param>
/// <param name="BaseType">The type it stands for, as written, such as <c>nvarchar(20)</c>.</param>
/// <param name="Nullability">
/// What its declaration says about null. A column of the type that says neither NULL nor NOT NULL takes it.
/// </param>
public sealed record AliasType(ObjectName Name, SourceSpan Span, SqlType BaseType, DeclaredNullability Nullability) : UserType(Name, Span);
