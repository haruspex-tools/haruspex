using Haruspex.Collections;

namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>
/// A keyword's value in the model: what JSON Schema's keywords hold, and nothing more. Records compare by value, so two
/// schemas built the same way are equal, which is how a union's repeated variants are found.
/// </summary>
internal abstract record SchemaValue
{
    private SchemaValue()
    {
    }

    /// <summary>A string, such as a <c>type</c>, a <c>format</c> or a <c>pattern</c>.</summary>
    public sealed record Text(string Value) : SchemaValue;

    /// <summary>A number, as exact decimal text, written as it is: never rounded to a double on the way out.</summary>
    public sealed record Number(string Value) : SchemaValue;

    /// <summary>A whole number, such as a length or a count of items.</summary>
    public sealed record Count(int Value) : SchemaValue;

    /// <summary><c>true</c> or <c>false</c>, as a value or as a schema that accepts everything or nothing.</summary>
    public sealed record Boolean(bool Value) : SchemaValue;

    /// <summary>The JSON <c>null</c>, as an item of an <c>enum</c>.</summary>
    public sealed record Null : SchemaValue;

    /// <summary>A list: of types, of required keys, of an <c>enum</c>'s items, or of schemas.</summary>
    public sealed record List(EquatableArray<SchemaValue> Items) : SchemaValue;

    /// <summary>A schema inside a keyword: <c>items</c>, <c>contentSchema</c>, <c>additionalProperties</c>.</summary>
    public sealed record Nested(Schema Schema) : SchemaValue;

    /// <summary>Schemas by name, in order: <c>properties</c>.</summary>
    public sealed record Properties(EquatableArray<SchemaProperty> Items) : SchemaValue;
}
