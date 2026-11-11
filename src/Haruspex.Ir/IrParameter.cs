namespace Haruspex.Ir;

/// <param name="Name">The parameter's name as declared, including any sigil such as <c>@</c>.</param>
/// <param name="Type">The declared type.</param>
/// <param name="HasDefault">Whether the caller may omit it.</param>
/// <param name="IsOutput">Whether it is an output parameter.</param>
public sealed record IrParameter(string Name, IrType Type, bool HasDefault, bool IsOutput);
