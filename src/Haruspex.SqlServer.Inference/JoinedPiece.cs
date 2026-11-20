using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// <c>concat_ws(separator, a, b, …)</c>: the arguments that are not NULL, in order, with the separator between each two, so any
/// subset of those that may be NULL, none included (observed:
/// Concat_ws_skips_null_values_so_quoted_literals_make_any_subset_and_none_makes_empty_brackets).
/// </summary>
/// <param name="Separator">The pieces of the separator's text.</param>
/// <param name="Items">Each argument: its pieces, and whether it may be NULL and so left out.</param>
internal sealed record JoinedPiece(EquatableArray<TextPiece> Separator, EquatableArray<JoinedPiece.Item> Items) : TextPiece
{
    /// <summary>One argument of <c>concat_ws</c>.</summary>
    /// <param name="Pieces">Its text when it is not NULL.</param>
    /// <param name="MayBeAbsent">Whether it may be NULL, which leaves it out.</param>
    internal sealed record Item(EquatableArray<TextPiece> Pieces, bool MayBeAbsent);
}
