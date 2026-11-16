namespace Haruspex.Ir;

/// <param name="Name">The parameter's name as declared, including any sigil such as <c>@</c>.</param>
/// <param name="Type">
/// The declared type, with its facts: what the parameter keeps of a value a caller sends, and so the rule a client checks a
/// value against before sending it (a longer string is cut short, a decimal of more places rounded).
/// </param>
/// <param name="HasDefault">Whether the caller may omit it.</param>
/// <param name="IsOutput">Whether it is an output parameter.</param>
public sealed record IrParameter(string Name, IrType Type, bool HasDefault, bool IsOutput);
