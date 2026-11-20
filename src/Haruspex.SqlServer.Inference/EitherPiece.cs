using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Text that is one of several: the arguments of <c>COALESCE</c> or <c>ISNULL</c>, the results of a <c>CASE</c>, or a value
/// that may be NULL where NULL counts as empty (in <c>concat</c>, or <c>+</c> with <c>CONCAT_NULL_YIELDS_NULL</c> off), whose
/// one alternative is no pieces at all.
/// </summary>
/// <param name="Alternatives">The pieces of each.</param>
internal sealed record EitherPiece(EquatableArray<EquatableArray<TextPiece>> Alternatives) : TextPiece;
