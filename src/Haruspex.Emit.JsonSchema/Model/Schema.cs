using Haruspex.Collections;

namespace Haruspex.Emit.JsonSchema.Model;

/// <summary>
/// One schema: its keywords by name, and the notes its <c>description</c> is written from. The printer decides the keywords'
/// order (<see cref="Keyword.Order"/>), so the lowering adds them in whatever order reads best, and two schemas with the same
/// keywords are equal however they were built.
/// </summary>
/// <param name="Keywords">The keywords by name, each from <see cref="Keyword"/>; <c>description</c> is never one.</param>
/// <param name="Notes">The sentences of its description, in the order its reader meets them.</param>
internal sealed record Schema(EquatableMap<string, SchemaValue> Keywords, EquatableArray<SchemaNote> Notes)
{
    /// <summary><c>{}</c>: any value at all, null included.</summary>
    public static Schema Any { get; } = new([], []);

    /// <summary><c>{ "not": {} }</c>: no value at all, as an empty value set allows.</summary>
    public static Schema Never { get; } = Any.With(Keyword.Not, new SchemaValue.Nested(Any));

    /// <summary>A schema of one JSON type: <c>{ "type": "string" }</c>.</summary>
    public static Schema OfType(string type) => Any.With(Keyword.Type, new SchemaValue.Text(type));

    /// <summary>A reference to a definition in the same document: <c>{ "$ref": "#/$defs/OrderRow" }</c>.</summary>
    public static Schema Reference(string name) => Reference(null, name);

    /// <summary>
    /// A reference to a definition in <paramref name="document"/>, relative to the referring document's own name, or in the same
    /// document when null: <c>{ "$ref": "shared.jsonschema.json#/$defs/OrderTotal" }</c>.
    /// </summary>
    public static Schema Reference(string? document, string name) => Any.With(Keyword.Ref, new SchemaValue.Text(document + Keyword.DefinitionsPointer + name));

    /// <summary>This schema with <paramref name="keyword"/> set to <paramref name="value"/>.</summary>
    public Schema With(string keyword, SchemaValue value) => this with { Keywords = Keywords.SetItem(keyword, value) };

    /// <summary>This schema with <paramref name="keyword"/> set when <paramref name="value"/> is given, and as it is otherwise.</summary>
    public Schema With(string keyword, int? value) => value is { } count ? With(keyword, new SchemaValue.Count(count)) : this;

    /// <summary>This schema with a note added after the others.</summary>
    public Schema Noted(SchemaNote note) => this with { Notes = [.. Notes, note] };

    /// <summary>This schema with notes added after the others.</summary>
    public Schema Noted(IEnumerable<SchemaNote> notes) => this with { Notes = [.. Notes, .. notes] };

    /// <summary>Whether it accepts any value, which a reference or a value set doesn't.</summary>
    public bool IsAny => Keywords.Count == 0;

    /// <summary>
    /// This schema, accepting null too, in the plainest form that says so: <c>null</c> added to its <c>type</c> or its <c>enum</c>,
    /// a variant added to its <c>anyOf</c>, or else <c>anyOf</c> of it and <c>{ "type": "null" }</c>, its notes kept outside.
    /// <c>{}</c> already accepts null, and a schema that accepts nothing accepts only null.
    /// </summary>
    public Schema OrNull()
    {
        var nullType = new SchemaValue.Text("null");
        if (IsAny)
            return this;
        if (this == Never with { Notes = Notes })
            return OfType("null") with { Notes = Notes };
        if (Keywords.GetValueOrDefault(Keyword.Enum) is SchemaValue.List items)
            return With(Keyword.Enum, new SchemaValue.List([.. items.Items, new SchemaValue.Null()]));
        if (Keywords.GetValueOrDefault(Keyword.Type) is SchemaValue.Text type)
            return With(Keyword.Type, new SchemaValue.List([type, nullType]));
        var alone = OfType("null");
        if (Keywords.Count == 1 && Keywords.GetValueOrDefault(Keyword.AnyOf) is SchemaValue.List variants)
            return With(Keyword.AnyOf, new SchemaValue.List([.. variants.Items, new SchemaValue.Nested(alone)]));
        return Any.With(Keyword.AnyOf, new SchemaValue.List([new SchemaValue.Nested(this with { Notes = [] }), new SchemaValue.Nested(alone)])) with { Notes = Notes };
    }
}
