namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Text that is exactly one JSON value of known content: live JSON (a constructor, a <c>FOR JSON</c> subquery with its array
/// wrapper, <c>json_query</c>), one row of <c>FOR JSON … WITHOUT_ARRAY_WRAPPER</c>, or a place or call holding JSON assembled here.
/// </summary>
/// <param name="Content">The JSON's shape.</param>
internal sealed record JsonPiece(Shape Content) : TextPiece;
