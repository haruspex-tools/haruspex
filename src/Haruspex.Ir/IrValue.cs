namespace Haruspex.Ir;

/// <summary>One item of a value set.</summary>
/// <param name="Text">A string's value, or a number's exact decimal text.</param>
/// <param name="IsString">Whether it is a string; otherwise a number.</param>
public sealed record IrValue(string Text, bool IsString);
