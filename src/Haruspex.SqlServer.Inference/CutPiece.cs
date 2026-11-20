using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Text that may be longer than its type holds, which cuts it short without an error: the first part of
/// <paramref name="Pieces"/>. Cut text at the end of what is read is a strict prefix of a JSON array, never JSON itself, so the
/// read fails and it describes no call that succeeds. Text after a cut would follow a cut that may fall anywhere, so the typer
/// reads no pieces there (<c>Typer.JsonAssembled.cs</c>), with one exception: text cut at the most a string that is not
/// <c>max</c> holds stays at that limit through every later <c>+</c> or <c>concat</c> that is not <c>max</c> either, so
/// what follows is cut away too.
/// </summary>
/// <param name="Pieces">The pieces of the text before it is cut.</param>
/// <param name="AtTypeLimit">
/// Whether it is cut at the most a string that is not <c>max</c> holds (4,000 characters, 8,000 bytes), by <c>+</c> or
/// <c>concat</c>; otherwise by a conversion to a shorter type.
/// </param>
internal sealed record CutPiece(EquatableArray<TextPiece> Pieces, bool AtTypeLimit) : TextPiece;
