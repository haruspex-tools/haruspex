using Haruspex.Ir;

namespace Haruspex.SqlServer.Inference;

/// <summary>What a call of a scalar function gives, the same for every call (no specialisation by argument).</summary>
/// <param name="Declared">The declared return type's shape, which is all a call gives when <paramref name="Content"/> is null.</param>
/// <param name="ReturnsJson">Whether the return type is <c>json</c>: a call is then live JSON, not text (observed).</param>
/// <param name="Content">
/// The JSON the function returns, when its body is one of the forms read (see <see cref="FunctionSummaries"/>); null when
/// only the declared type is known. For a function that calls itself, its node is a <see cref="RecursiveShape"/>.
/// </param>
/// <param name="NullableBecause">Why the body's result may be null (with <paramref name="Content"/>), such as FOR JSON over no rows.</param>
/// <param name="NullOnNullInput">Whether it is declared <c>WITH RETURNS NULL ON NULL INPUT</c>.</param>
internal sealed record FunctionReturn(Shape Declared, bool ReturnsJson, Shape? Content, IReadOnlyList<IrNullReason> NullableBecause, bool NullOnNullInput);
