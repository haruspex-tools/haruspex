using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A sequence (<c>CREATE SEQUENCE</c>): what <c>NEXT VALUE FOR</c> draws its numbers from, each of the sequence's type.
/// </summary>
/// <remarks>Only its type is kept: its start, step, bounds and cycling change which numbers it gives, never their type.</remarks>
/// <param name="Name">Its name.</param>
/// <param name="Span">Where it is created.</param>
/// <param name="Type">
/// Its type as declared: an integer type, <c>decimal</c> or <c>numeric</c> of scale 0, or an alias type of one of them, the
/// only types the server accepts (11702); <c>bigint</c> when it declares none, as the server takes it; null when the source
/// declares one that can't be read.
/// </param>
public sealed record Sequence(ObjectName Name, SourceSpan Span, SqlType? Type) : SchemaObject(Name, Span)
{
    /// <summary>The type a sequence that declares none has: <c>bigint</c>.</summary>
    public static SqlType Undeclared { get; } = new(SqlTypeKind.BuiltIn, "bigint", []);
}
