using Haruspex.Diagnostics;
namespace Haruspex.SqlServer.Inference;

/// <summary>Text written in the SQL as a string literal: <c>N'['</c>, <c>','</c>, <c>'"retail"'</c>.</summary>
/// <param name="Text">The literal's value.</param>
/// <param name="National">Whether it is an <c>N'…'</c> literal, whose text is not converted to a code page (<see cref="JsonLiterals"/>).</param>
/// <param name="At">Where it is written: the source of a value set its JSON strings and numbers have.</param>
internal sealed record LiteralPiece(string Text, bool National, SourceLocation At) : TextPiece;
