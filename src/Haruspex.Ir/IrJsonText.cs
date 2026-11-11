namespace Haruspex.Ir;

/// <summary>
/// A string that holds JSON. Distinct from embedded JSON: once JSON is stored or returned as a column, passes through
/// a scalar function or is built <c>WITHOUT_ARRAY_WRAPPER</c>, a consumer receives text and must parse it.
/// </summary>
/// <param name="Content">
/// What the text parses to, when it was built here: a non-null string holds JSON of this type. Null when the JSON is
/// data (a <c>json</c> column, a parameter) or its shape is unknown, which claims nothing beyond "JSON".
/// </param>
public sealed record IrJsonText(IrType? Content = null) : IrType;
