namespace Haruspex.Ir;

/// <summary>One vocabulary's table read backwards: an <c>IrWireNames.TryParse</c> overload, as the reader passes it.</summary>
/// <param name="name">The name in the file.</param>
/// <param name="value">The member it names.</param>
/// <returns>Whether it names one.</returns>
internal delegate bool IrWireNameParser<T>(string name, out T value)
    where T : struct, Enum;
