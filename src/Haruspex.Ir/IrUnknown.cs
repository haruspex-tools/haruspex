namespace Haruspex.Ir;

/// <summary>Something that could not be typed.</summary>
/// <param name="Object">The id of the object it belongs to.</param>
/// <param name="Path">Where in the object, such as <c>results[0].lines[].name</c>.</param>
/// <param name="Reason">One of <see cref="IrUnknownReasons"/>.</param>
/// <param name="Detail">A human-readable explanation.</param>
/// <param name="At">The source location of the construct, as <c>path:line:column</c>.</param>
public sealed record IrUnknown(string Object, string Path, string Reason, string Detail, string At);
