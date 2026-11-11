namespace Haruspex.Ir;

/// <param name="Dialect">The SQL dialect of the source, such as <c>tsql</c>.</param>
public sealed record IrSource(string Dialect);
