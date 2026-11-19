using System.Globalization;
using Haruspex.Collections;
using Haruspex.Emit.Zod.Model;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Emit.Zod.Lowering;

/// <summary>
/// Each IR type as a Zod expression, read as its encoding delivers it: the second stage's work on one type. It decides what
/// each fact becomes in Zod; it writes no text.
/// </summary>
/// <param name="types">The document's named types, whose keys a union's variants are told apart by.</param>
/// <param name="schemaOf">The binding each named type's schema is declared under: its own, or that of the shape it shares.</param>
internal sealed class TypeLowering(IReadOnlyDictionary<string, IrObjectType> types, Func<string, string> schemaOf)
{
    /// <summary>The schema of a value of <paramref name="type"/> that is not null, as <paramref name="encoding"/> delivers it.</summary>
    public ZodExpression Type(IrType type, IrEncoding encoding) => type switch
    {
        IrScalar scalar => Scalar(scalar, encoding),
        IrArray array => Array(array, encoding),
        IrTuple tuple => Z("tuple", new ArrayLiteral([.. tuple.Items.Select(e => Nullable(Type(e.Type, encoding), e.Nullable, e.Type))])),
        IrRecord record => Z("record", Z("string"), Nullable(Type(record.Values, encoding), record.ValuesNullable, record.Values)),
        IrUnion union => Union(union, encoding),
        IrRef reference => Reference(reference),
        IrImport import => new Name(import.Name),

        // What the text holds is JSON, even in a rowset.
        IrJsonText { Content: { } content } => new Noted(new JsonText(Type(content, IrEncoding.Json)), new ZodNote("A string holding JSON, checked against the schema inside.")),
        IrJsonText => new Noted(Z("string"), new ZodNote("A string holding JSON.")),
        IrUnknownType unknown => new Noted(Z("unknown"), new ZodNote($"Not known ({IrWireNames.Of(unknown.Reason)}); the IR's unknowns say why.")),
    };

    /// <summary>
    /// A field's schema. Presence and nullability are separate: a key that is left out when null is <c>.optional()</c>
    /// (never null), a key that is kept as null is <c>.nullable()</c> (never absent), and a key that may be left out whatever its
    /// value, in a document combining two databases, is <c>.optional()</c> and <c>.nullable()</c> as well when it may be null.
    /// </summary>
    public ZodExpression Field(IrField field, IrEncoding encoding)
    {
        var value = Type(field.Type, encoding);
        return field.Presence switch
        {
            IrPresence.OmittedWhenNull when field.Nullable => value.Then("optional"),
            IrPresence.Optional => Nullable(value, field.Nullable, field.Type).Then("optional"),
            _ => Nullable(value, field.Nullable, field.Type),
        };
    }

    /// <summary>The schema a named type is declared under.</summary>
    public Name Reference(IrRef reference) => new(schemaOf(reference.Name));

    /// <summary>
    /// An array, with its items' nullability and its length when the IR knows them (<c>json_array</c> and the aggregates; an
    /// array FOR JSON builds has neither).
    /// </summary>
    private ZodExpression Array(IrArray array, IrEncoding encoding)
    {
        var code = Z("array", Nullable(Type(array.Items, encoding), array.ItemsNullable, array.Items));
        if (array.MinItems is { } min)
            code = code.Then("min", new Number(min.ToString(CultureInfo.InvariantCulture)));
        if (array.MaxItems is { } max)
            code = code.Then("max", new Number(max.ToString(CultureInfo.InvariantCulture)));
        return code;
    }

    /// <summary>
    /// A union, without the variants another already accepts (<see cref="Unions"/>). <c>z.union</c> returns the first variant that accepts a value, and a <c>z.object</c> strips the keys it doesn't
    /// know, so a variant would accept another's object and drop its keys: a key left out when null is <c>.optional()</c>, so
    /// even a variant with more keys accepts an object of fewer, and no order prevents it. So each object variant (or array
    /// of one) also rejects every key that only the other variants have, <c>.extend({ k: z.never().optional() })</c>: an
    /// object takes the variant whose keys it has, and keeps them all. Keys no variant knows are still stripped (or refused,
    /// for strict objects), as everywhere else.
    /// </summary>
    private ZodExpression Union(IrUnion union, IrEncoding encoding)
    {
        var keys = union.Variants.Select(ObjectKeys).ToList();
        return Unions.Of(SupersetsFirst(union.Variants).Select(i => Variant(union.Variants[i], keys[i], keys.Where((_, j) => j != i))));

        ZodExpression Variant(IrType variant, IReadOnlyList<string>? mine, IEnumerable<IReadOnlyList<string>?> others)
        {
            var theirs = mine is null
                ? []
                : others.OfType<IReadOnlyList<string>>().SelectMany(k => k).Where(k => !mine.Contains(k, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            if (theirs.Count == 0)
                return Type(variant, encoding);
            var never = new ObjectLiteral([.. theirs.Select(k => new ZodProperty(k, Z("never").Then("optional")))]);
            return variant switch
            {
                IrRef reference => Reference(reference).Then("extend", never),
                IrArray { Items: IrRef item } => Z("array", Reference(item).Then("extend", never)),
                _ => throw new InvalidOperationException("Only an object, or an array of one, has keys to tell apart."),
            };
        }
    }

    /// <summary>The keys of a variant that is an object, or a plain array of one; null for any other.</summary>
    private List<string>? ObjectKeys(IrType type) => type switch
    {
        IrRef reference when types.TryGetValue(reference.Name, out var target) => [.. target.Fields.Select(f => f.Name)],
        IrArray { Items: IrRef reference, MinItems: null, MaxItems: null, ItemsNullable: false } => ObjectKeys(reference),
        _ => null,
    };

    /// <summary>
    /// The positions of a union's variants, those of more keys first. <c>z.union</c> returns the first variant that accepts a
    /// value, and a <c>z.object</c> strips the keys it doesn't know, so an object variant whose keys are a subset of another's
    /// would accept the other's values and drop their extra keys. A rowset's columns are always all there, so a row only fits
    /// a variant whose keys it has, and putting more keys first makes the one it takes its own. An array of objects is ordered
    /// by its objects. Equal counts keep the IR's order.
    /// </summary>
    private IEnumerable<int> SupersetsFirst(EquatableArray<IrType> variants) =>
        Enumerable.Range(0, variants.Count).OrderByDescending(i => KeyCount(variants[i])).ThenBy(i => i);

    private int KeyCount(IrType type) => type switch
    {
        IrRef reference when types.TryGetValue(reference.Name, out var target) => target.Fields.Count,
        IrArray array => KeyCount(array.Items),
        _ => 0,
    };

    /// <summary>A value that may be null; <c>z.unknown()</c> already accepts null.</summary>
    private static ZodExpression Nullable(ZodExpression value, bool nullable, IrType type) =>
        nullable && type is not IrUnknownType ? value.Then("nullable") : value;

    /// <summary>
    /// A scalar as the caller receives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In JSON, SQL Server writes every number as a JSON number, and dates, times and uniqueidentifiers as strings in a fixed
    /// format, so those are validated strings. They are deliberately not converted to <c>Date</c>: a <c>date</c> or an
    /// offset-less <c>datetime2</c> has no time zone, and <c>new Date('2026-11-09')</c> is UTC midnight, which shows as the
    /// previous day west of Greenwich. Conversion could become an emitter option later. Fractional-second precision is not
    /// constrained. The differential tests pinned what FOR JSON writes (the IR's README, "Scalars in JSON"): a fraction of
    /// exactly the type's scale, left out when zero, and a zero offset as <c>Z</c>. These validators accept every such string
    /// (checked against zod 4.6.7). A uniqueidentifier is <c>z.guid()</c>, not <c>z.uuid()</c>, because its value need not
    /// be an RFC 9562 UUID.
    /// </para>
    /// <para>
    /// In a rowset, the mssql driver returns dates and times as <c>Date</c> and <c>bigint</c> as a string, because a
    /// JavaScript number cannot hold every 64-bit value. A binary value is a Node <c>Buffer</c> in a rowset and base64 text in
    /// JSON (<see cref="Facets.Bytes"/>).
    /// </para>
    /// </remarks>
    private static ZodExpression Scalar(IrScalar scalar, IrEncoding encoding)
    {
        var json = encoding == IrEncoding.Json;
        return scalar.Kind switch
        {
            IrScalarKind.String => Facets.String(scalar),
            IrScalarKind.Boolean => Z("boolean"),
            IrScalarKind.Uuid => Z("guid"),
            IrScalarKind.Date => json ? Iso("date") : Z("date"),
            IrScalarKind.Time => json ? Iso("time") : Z("date"),
            IrScalarKind.Datetime when !json => Z("date"),
            IrScalarKind.Datetime => scalar.Format switch
            {
                IrFormat.Local => Iso("datetime", new ObjectLiteral([new ZodProperty("local", new ZodExpression.Boolean(true))])),
                IrFormat.Offset => Iso("datetime", new ObjectLiteral([new ZodProperty("offset", new ZodExpression.Boolean(true))])),

                // Without a format the offset is not known, so no format is claimed.
                null => Z("string"),
                _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar.Format, "A date and time's format is local or offset."),
            },
            IrScalarKind.Number => Facets.Number(scalar, json),
            IrScalarKind.Bytes => Facets.Bytes(scalar, json),
            _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar.Kind, "A scalar kind outside the IR's closed set."),
        };
    }

    /// <summary>One of Zod's ISO 8601 string formats: <c>z.iso.date()</c>.</summary>
    private static Call Iso(string format, params ZodExpression[] arguments) => new(new Name(ZodModule.ZodName + ".iso"), format, [.. arguments]);
}
