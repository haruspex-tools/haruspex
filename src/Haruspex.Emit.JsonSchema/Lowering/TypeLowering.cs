using Haruspex.Collections;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// Each IR type as a schema: the second stage's work on one type. It decides what each node becomes in JSON Schema; it writes no
/// text. Named object types are referenced by <c>$ref</c>, so recursion needs nothing more.
/// </summary>
/// <param name="types">The document's named types, whose keys a union's object variants are told apart by.</param>
/// <param name="unknowns">What the document says about each unknown, for the note on its schema.</param>
/// <param name="declared">The claims the config file made, for the note on the field each is at.</param>
internal sealed class TypeLowering(IReadOnlyDictionary<string, IrObjectType> types, UnknownDetails unknowns, DeclaredClaims declared)
{
    /// <summary>The schema of a value of <paramref name="type"/> that is not null.</summary>
    /// <param name="place">Where the value is, for an unknown's note; null when the type has no place in the IR (built by hand).</param>
    public Schema Type(IrType type, Place? place) => type switch
    {
        IrScalar scalar => Scalars.Of(scalar),
        IrArray array => Array(array, place),
        IrTuple tuple => Tuple(tuple, place),
        IrRecord record => Schema.OfType("object")
            .With(Keyword.AdditionalProperties, new SchemaValue.Nested(Nullable(Type(record.Values, place?.Then(new IrPathValues())), record.ValuesNullableBecause)))
            .Noted(new SchemaNote("An object whose keys are data.")),
        IrUnion union => Union(union, place),
        IrRef reference => Schema.Reference(reference.Name),
        IrImport import => Schema.Any.Noted(new SchemaNote(
            $"Declared by the config file to be {import.Name} from {import.From}, an application's schema this document can't refer to, so any value is accepted here.")),
        IrJsonText text => JsonText(text, place),
        IrUnknownType unknown => Schema.Any.Noted(unknowns.Note(unknown.Reason, place)),
    };

    /// <summary>
    /// A field's schema. Presence and nullability are separate: a key left out when null is not <c>required</c> and never null,
    /// and a key kept as null is required and may be null. Which keys are required is the object's (<see cref="IsRequired"/>).
    /// </summary>
    public Schema Field(IrField field, Place? place)
    {
        var at = place?.Then(new IrPathKey(field.Name));
        var value = Type(field.Type, at);
        var schema = field.Presence switch
        {
            IrPresence.OmittedWhenNull => value.Noted(Notes(Origins.Nulls("Left out when null", field.NullableBecause))),
            IrPresence.Optional => Nullable(value, field.NullableBecause).Noted(LeftOut),
            _ => Nullable(value, field.NullableBecause),
        };
        return schema.Noted(declared.At(at));
    }

    /// <summary>Why a key of a document combining two databases may be left out whatever its value.</summary>
    private static readonly SchemaNote LeftOut = new("May be left out: the two databases these types combine don't agree on it.");

    /// <summary>The notes of the claims the config file made at <paramref name="place"/>, such as a procedure's declared results.</summary>
    public IEnumerable<SchemaNote> Declared(Place place) => declared.At(place);

    /// <summary>Whether a field's key is always there: in a rowset, and in JSON when its value is never null.</summary>
    public static bool IsRequired(IrField field) => field.Presence == IrPresence.Always || (field.Presence == IrPresence.OmittedWhenNull && !field.Nullable);

    /// <summary>The schema, accepting null too when there is a reason, which its note gives.</summary>
    public static Schema Nullable(Schema schema, EquatableArray<IrNullReason> reasons) =>
        reasons.Count == 0 ? schema : schema.OrNull().Noted(Notes(Origins.Nulls("May be null", reasons)));

    private static IEnumerable<SchemaNote> Notes(SchemaNote? note) => note is null ? [] : [note];

    /// <summary>An array, with its items' nullability and its length when the IR knows them.</summary>
    private Schema Array(IrArray array, Place? place) =>
        Schema.OfType("array")
            .With(Keyword.Items, new SchemaValue.Nested(Nullable(Type(array.Items, place?.Then(new IrPathItems())), array.ItemsNullableBecause)))
            .With(Keyword.MinItems, array.MinItems)
            .With(Keyword.MaxItems, array.MaxItems);

    /// <summary>
    /// A tuple: every position, each with its own type, and no more (<c>prefixItems</c>, <c>"items": false</c>, and as many
    /// <c>minItems</c>). A tuple of none is the empty array, since <c>prefixItems</c> must list at least one.
    /// </summary>
    private Schema Tuple(IrTuple tuple, Place? place)
    {
        if (tuple.Items.Count == 0)
            return Schema.OfType("array").With(Keyword.MaxItems, 0);
        var positions = tuple.Items.Select((element, i) => new SchemaValue.Nested(Nullable(Type(element.Type, place?.Then(new IrPathIndex(i))), element.NullableBecause)));
        return Schema.OfType("array")
            .With(Keyword.PrefixItems, new SchemaValue.List([.. positions]))
            .With(Keyword.Items, new SchemaValue.Boolean(false))
            .With(Keyword.MinItems, tuple.Items.Count);
    }

    /// <summary>
    /// A union: <c>anyOf</c> its variants, each written once, sharing the union's place. Each variant that is an object (or an
    /// array of one) also refuses the keys only the other variants have, as the Zod emitter's variants do: the IR says a value is
    /// one variant or another, so an object with keys of two is none of them, and in strip mode, where an object allows keys it
    /// doesn't list, only the refusal says so. Keys no variant knows are still allowed (or refused, for strict objects), as
    /// everywhere else.
    /// </summary>
    private Schema Union(IrUnion union, Place? place)
    {
        var keys = union.Variants.Select(ObjectKeys).ToList();
        var variants = union.Variants
            .Select((variant, i) => Refusing(Type(variant, place), variant, Theirs(keys[i], keys.Where((_, j) => j != i))))
            .Distinct()
            .ToList();
        return variants.Count == 1
            ? variants[0]
            : Schema.Any.With(Keyword.AnyOf, new SchemaValue.List([.. variants.Select(v => new SchemaValue.Nested(v))]));
    }

    /// <summary>The keys the other variants have that <paramref name="mine"/> lacks, in their order; none for a variant that isn't an object.</summary>
    private static List<string> Theirs(List<string>? mine, IEnumerable<List<string>?> others) =>
        mine is null
            ? []
            : [.. others.OfType<List<string>>().SelectMany(k => k).Where(k => !mine.Contains(k, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];

    /// <summary>The variant's schema, its object refusing <paramref name="keys"/>: beside its <c>$ref</c>, or its items'.</summary>
    private static Schema Refusing(Schema schema, IrType variant, List<string> keys)
    {
        if (keys.Count == 0)
            return schema;
        // The type beside the properties is what the reference says already; a validator in strict mode (Ajv's) wants keywords
        // of objects to say they apply to one.
        var refused = new SchemaValue.Properties([.. keys.Select(k => new SchemaProperty(k, Schema.Never))]);
        Schema Refuse(Schema target) => target.With(Keyword.Type, new SchemaValue.Text("object")).With(Keyword.Properties, refused);
        return variant switch
        {
            IrRef => Refuse(schema),
            IrArray { Items: IrRef } when schema.Keywords[Keyword.Items] is SchemaValue.Nested items =>
                schema.With(Keyword.Items, new SchemaValue.Nested(Refuse(items.Schema))),
            _ => throw new InvalidOperationException("Only an object, or an array of one, has keys to refuse."),
        };
    }

    /// <summary>The keys of a variant that is an object, or a plain array of one; null for any other.</summary>
    private List<string>? ObjectKeys(IrType type) => type switch
    {
        IrRef reference when types.TryGetValue(reference.Name, out var target) => [.. target.Fields.Select(f => f.Name)],
        IrArray { Items: IrRef reference, MinItems: null, MaxItems: null, ItemsNullable: false } => ObjectKeys(reference),
        _ => null,
    };

    /// <summary>
    /// JSON text: a string, <c>contentMediaType</c> JSON, and <c>contentSchema</c> what it parses to when that is known. The
    /// content schema is an annotation (draft 2020-12, "A Vocabulary for the Contents of String-Encoded Data"), so a note says it
    /// is checked only when a validator is asked to.
    /// </summary>
    private Schema JsonText(IrJsonText text, Place? place)
    {
        var schema = Schema.OfType("string").With(Keyword.ContentMediaType, new SchemaValue.Text("application/json"));
        return text.Content is { } content
            ? schema.With(Keyword.ContentSchema, new SchemaValue.Nested(Type(content, place?.Then(new IrPathJson()))))
                .Noted(new SchemaNote("A string holding JSON, which contentSchema describes.", JsonSchemaCaveat.ContentSchemas))
            : schema.Noted(new SchemaNote("A string holding JSON."));
    }
}
