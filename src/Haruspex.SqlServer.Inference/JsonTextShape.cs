namespace Haruspex.SqlServer.Inference;

/// <summary>
/// A string holding JSON: what a JSON value becomes once a driver returns it as a column, once it passes through a
/// scalar function, or when FOR JSON builds it without its array wrapper.
/// </summary>
/// <param name="Content">What the text parses to when it was built here, which <c>json_query</c> makes live again; null when unknown.</param>
internal sealed record JsonTextShape(Shape? Content = null) : Shape;
