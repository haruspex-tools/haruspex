using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// <c>string_agg(item, separator)</c>, not null: one or more items' text with the separator between each two. NULL items are
/// skipped; whether the whole may be NULL is the value's, and where a NULL counts as empty the pieces around it say so
/// (<see cref="EitherPiece"/>).
/// </summary>
/// <param name="Item">The pieces of each item's text.</param>
/// <param name="Separator">The pieces of the separator's text.</param>
internal sealed record ListPiece(EquatableArray<TextPiece> Item, EquatableArray<TextPiece> Separator) : TextPiece;
