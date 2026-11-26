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

        // z.json() accepts every value JSON.parse gives and nothing else (observed in the pinned Zod: ZodObservationTests).
        IrAnyJson { ObjectOrArray: true } => new Noted(Z("union", new ArrayLiteral([Z("record", Z("string"), Z("json")), Z("array", Z("json"))])), new ZodNote("Any JSON object or array.")),
        IrAnyJson => new Noted(Z("json"), new ZodNote("Any JSON value.")),
        IrUnknownType unknown => new Noted(Z("unknown"), new ZodNote($"Not known ({IrWireNames.Of(unknown.Reason)}); the IR's unknowns say why.")),
    };

    /// <summary>
    /// A field's schema. Presence and nullability are separate: a key that is left out when null is <c>.optional()</c>
    /// (never null), a key that is kept as null is <c>.nullable()</c> (never absent), and a key that may be left out whatever its
    /// value, in a document combining two databases, is <c>.optional()</c> and <c>.nullable()</c> as well when it may be null.
    /// </summary>
    public ZodExpression Field(IrField field, IrEncoding encoding) => Field(field, Type(field.Type, encoding));

    /// <summary>A field's schema whose value is <paramref name="value"/>, with the field's presence and nullability.</summary>
    private static ZodExpression Field(IrField field, ZodExpression value)
    {
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
    private ZodExpression Array(IrArray array, IrEncoding encoding) => Array(array, Type(array.Items, encoding));

    /// <summary>An array whose items are <paramref name="items"/>, with the array's own facts.</summary>
    private static ZodExpression Array(IrArray array, ZodExpression items)
    {
        var code = Z("array", Nullable(items, array.ItemsNullable, array.Items));
        if (array.MinItems is { } min)
            code = code.Then("min", new Number(min.ToString(CultureInfo.InvariantCulture)));
        if (array.MaxItems is { } max)
            code = code.Then("max", new Number(max.ToString(CultureInfo.InvariantCulture)));
        return code;
    }

    /// <summary>
    /// A union, without the variants another already accepts (<see cref="Unions"/>). <c>z.union</c> returns the first variant that
    /// accepts a value, and a <c>z.object</c> strips the keys it doesn't know, so a variant would accept another's object and drop
    /// its keys: a key left out when null is <c>.optional()</c>, so even a variant with more keys accepts an object of fewer, and
    /// no order prevents it. So each variant also rejects, at every depth, the keys only the other variants' objects have at the
    /// same place (<see cref="Apart"/>): two branches with the same top-level keys whose nested <c>FOR JSON</c> lists differ are
    /// told apart by the nested objects' keys. An object takes the variant whose keys it has, and keeps them all. Keys no variant
    /// knows are still stripped (or refused, for strict objects), as everywhere else.
    /// </summary>
    private ZodExpression Union(IrUnion union, IrEncoding encoding) => Variants(union, [], encoding, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// <paramref name="union"/>'s variants in their order, each refusing the keys only its siblings and <paramref name="outer"/>
    /// have: a union nested in a variant is told apart from the other variants' types at its place as well as from its own.
    /// </summary>
    private ZodExpression Variants(IrUnion union, IReadOnlyList<IrType> outer, IrEncoding encoding, IReadOnlySet<string> seen) =>
        Unions.Of(SupersetsFirst(union.Variants).Select(i =>
            Apart(union.Variants[i], [.. union.Variants.Where((_, j) => j != i), .. outer], encoding, seen) ?? Type(union.Variants[i], encoding)));

    /// <summary>
    /// <paramref name="mine"/>'s schema refusing every key that only <paramref name="others"/> have at the same place, or null
    /// when it needs to refuse none. At an object it refuses the keys only the others' objects have,
    /// <c>.extend({ k: z.never().optional() })</c>, and goes on into each of its fields with the others' fields of that name; at an
    /// array, into its items with the others' items; at JSON text, into what it holds. A variant of another kind at the same
    /// place has no keys to tell apart. An open object refuses no key, since it may hold any, but its fields are still followed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A union among the others stands for each of its variants, and a union of <paramref name="mine"/> refuses, in each of its
    /// variants, what its siblings and the others have (<see cref="Variants"/>): <c>JSON_QUERY</c> over a <c>CASE</c> of two
    /// subqueries, or a <c>JSON_ARRAY</c> of differing objects, puts a union below a variant's top level.
    /// </para>
    /// <para>
    /// A refusal is written inline, so a pair of recursive types is followed once round its cycle and no further
    /// (<paramref name="seen"/>). Below that, a nested object of a recursive variant is told apart only by the keys of its own
    /// type, so it can still lose keys: with <c>{ kids: A[], a? }</c> and <c>{ kids: B[], b? }</c>, a <c>B</c> value whose own
    /// <c>b</c> is absent is taken by the first, and each child's <c>b</c> is stripped. Writing the whole cycle would need a
    /// schema of its own for each refusing copy, which no fixture or corpus has yet needed.
    /// </para>
    /// </remarks>
    private ZodExpression? Apart(IrType mine, IReadOnlyList<IrType> others, IrEncoding encoding, IReadOnlySet<string> seen)
    {
        others = [.. others.SelectMany(Flat)];
        switch (mine)
        {
            case IrUnion union:
            {
                var told = Variants(union, others, encoding, seen);
                return told == Type(union, encoding) ? null : told;
            }

            case IrRef reference when types.TryGetValue(reference.Name, out var target):
            {
                var objects = others.OfType<IrRef>().Where(r => r.Name != reference.Name && types.ContainsKey(r.Name)).Select(r => types[r.Name]).ToList();
                var pair = string.Join("|", [reference.Name, .. others.OfType<IrRef>().Select(r => r.Name).Order(StringComparer.Ordinal)]);
                if (objects.Count == 0 || seen.Contains(pair))
                    return null;
                var within = new HashSet<string>(seen, StringComparer.Ordinal) { pair };

                var properties = new List<ZodProperty>();
                foreach (var field in target.Fields)
                {
                    var theirs = objects.Select(o => o.Fields.FirstOrDefault(f => f.Name == field.Name)?.Type).OfType<IrType>().ToList();
                    if (theirs.Count > 0 && Apart(field.Type, theirs, encoding, within) is { } refused)
                        properties.Add(new ZodProperty(field.Name, Field(field, refused)));
                }

                if (!target.Open)
                {
                    properties.AddRange(objects.Where(o => !o.Open).SelectMany(o => o.Fields.Select(f => f.Name))
                        .Where(k => !target.Fields.Any(f => f.Name == k)).Distinct(StringComparer.Ordinal)
                        .Select(k => new ZodProperty(k, Z("never").Then("optional"))));
                }

                return properties.Count == 0 ? null : Reference(reference).Then("extend", new ObjectLiteral([.. properties]));
            }

            case IrArray array:
                return Apart(array.Items, [.. others.OfType<IrArray>().Select(a => a.Items)], encoding, seen) is { } items ? Array(array, items) : null;

            case IrJsonText { Content: { } content }:
                return Apart(content, [.. others.OfType<IrJsonText>().Select(t => t.Content).OfType<IrType>()], IrEncoding.Json, seen) is { } held
                    ? new Noted(new JsonText(held), new ZodNote("A string holding JSON, checked against the schema inside."))
                    : null;

            default:
                return null;
        }

        static IEnumerable<IrType> Flat(IrType type) => type is IrUnion union ? union.Variants.SelectMany(Flat) : [type];
    }

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
    /// previous day west of Greenwich. Conversion could become an emitter option later. Each is held to the form its type is
    /// written in (<see cref="DateTimes.Written"/>), which the differential tests pinned (the Inference README, "Scalars in JSON"):
    /// seconds always, a fraction of exactly the type's scale, left out when zero, and a zero offset as <c>Z</c>. Zod's ISO checks
    /// alone would accept forms the server never writes, a missing second, or an offset on a type that has none. A type the IR
    /// doesn't spell (a declared result's) keeps the kind's ISO check. A uniqueidentifier is <c>z.guid()</c>, not <c>z.uuid()</c>, because its value need not
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
            IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime when !json => Z("date"),
            IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime when DateTimes.Written(scalar) is { } written => written,
            IrScalarKind.Date => Iso("date"),
            IrScalarKind.Time => Iso("time"),
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
