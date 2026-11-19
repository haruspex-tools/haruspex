using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Emit.Zod.Model;

/// <summary>A property of an object schema or an object argument: a key and its value's schema.</summary>
/// <param name="Key">The key, as the data names it; the printer quotes one that is not an identifier.</param>
/// <param name="Value">The value's schema.</param>
/// <param name="Getter">
/// Whether the property is a getter, <c>get key() { return … }</c>: Zod 4's form for a reference back into the object's own
/// cycle, whose schema may not be defined yet where the property is written. TypeScript infers the type through a getter
/// without casts.
/// </param>
internal sealed record ZodProperty(string Key, ZodExpression Value, bool Getter = false)
{
    /// <summary>
    /// Why the field is what it is, a sentence a line (<see cref="IrFieldText"/>), written after the value's notes in
    /// the comment above the property; none when the setting asks for none.
    /// </summary>
    public EquatableArray<string> Explanation { get; init; } = [];
}
