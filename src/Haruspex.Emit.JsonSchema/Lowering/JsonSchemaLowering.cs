using Haruspex.Collections;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Emit.JsonSchema.Planning;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// The second stage: the plan's every entry as a definition (<see cref="JsonSchemaDocument"/>). What each IR type becomes is
/// <see cref="TypeLowering"/>'s; this decides what a procedure's results and a named object type become, and what the reader of
/// the document is told about them.
/// </summary>
internal static class JsonSchemaLowering
{
    /// <summary>The document the plan describes.</summary>
    public static JsonSchemaDocument Lower(JsonSchemaPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Lower(plan, plan.Entries, null, "Each procedure's results are the schema in $defs under its name, and each named type is there too.");
    }

    /// <summary>
    /// The document split into a document per module (<see cref="IrModules"/>), by module, with <see cref="IrModules.Shared"/>'s
    /// when two or more modules reach a named type: a procedure is defined in its module's document, and a named type in that of the
    /// one module that reaches it, or else in the shared one, which a module refers to relative to its own name
    /// (<c>docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md</c>). Every definition is the one the
    /// unsplit document has, but for where a reference points.
    /// </summary>
    public static EquatableMap<string, JsonSchemaDocument> Lower(JsonSchemaPlan plan, IrModules modules)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(modules);
        var shared = plan.Options.SharedDocument;
        var placed = plan.Entries.Select(entry => (Entry: entry, Home: entry switch
        {
            JsonSchemaPlanEntry.NamedType named => modules.HomeOf(named.Name),
            JsonSchemaPlanEntry.Procedure procedure => modules.Of(procedure.Object),
        })).ToList();
        return placed.Select(p => p.Home).Distinct(StringComparer.Ordinal).ToEquatableMap(file => file, file =>
        {
            var entries = placed.Where(p => p.Home == file).Select(p => p.Entry).ToList();
            var refersToShared = entries.SelectMany(Reached).Any(type => modules.HomeOf(type) != file);
            var what = file == IrModules.Shared
                ? $"The named types two or more modules of this config share, each defined once in $defs under its name; the modules refer to them by a $ref relative to their own names: {shared}#/$defs/ and the type's name."
                : refersToShared
                    ? $"Each procedure's results are the schema in $defs under its name, and each named type that only this module's procedures reach is there too; one that other modules' procedures reach as well is in {shared}, beside this document, referred to by a $ref relative to its name."
                    : "Each procedure's results are the schema in $defs under its name, and each named type is there too.";
            return Lower(plan, entries, type => modules.HomeOf(type) == file ? null : shared, what);
        });
    }

    /// <summary>The named types an entry's own schema refers to directly.</summary>
    private static IEnumerable<string> Reached(JsonSchemaPlanEntry entry) => entry switch
    {
        JsonSchemaPlanEntry.NamedType named => named.Type.Fields.SelectMany(f => IrTypeTree.References(f.Type)),
        JsonSchemaPlanEntry.Procedure procedure => (procedure.Object.Results ?? []).SelectMany(r => IrTypeTree.References(r.Type))
            .Concat(procedure.Object.Parameters.SelectMany(p => IrTypeTree.References(p.Type))),
    };

    /// <summary>The document of <paramref name="entries"/>, its description's second line <paramref name="what"/>.</summary>
    private static JsonSchemaDocument Lower(JsonSchemaPlan plan, IEnumerable<JsonSchemaPlanEntry> entries, Func<string, string?>? documentOf, string what)
    {
        var unknowns = new UnknownDetails(plan.Document);
        var types = new TypeLowering(plan.Document.Types, unknowns, new DeclaredClaims(plan.Document), documentOf);
        List<SchemaProperty> definitions =
        [
            .. entries.Select(entry => entry switch
            {
                JsonSchemaPlanEntry.NamedType named => new SchemaProperty(named.Name, ObjectType(named.Name, named.Type, plan, types)),
                JsonSchemaPlanEntry.Procedure procedure => new SchemaProperty(procedure.Name, Procedure(procedure.Object, types, unknowns)),
            }),
        ];
        return new JsonSchemaDocument([.. Description(plan.Document.Source, plan.Document.Version, what, definitions)], [.. definitions]);
    }

    /// <summary>
    /// The document's description: what made it, how to find a procedure's schema (<paramref name="what"/>), what every value is
    /// described as, what the callers' sessions were declared to set, and once each, the shared facts its notes rest on
    /// (<see cref="JsonSchemaCaveat"/>).
    /// </summary>
    private static IEnumerable<string> Description(IrSource source, string version, string what, IEnumerable<SchemaProperty> definitions)
    {
        yield return $"Generated by haruspex from IR {version}. Do not edit.";
        if (IrSourceText.Baseline(source) is { } combined)
            yield return combined;
        yield return what;
        yield return "Every value is described as SQL Server's FOR JSON writes it, a rowset's rows as an array of objects as FOR JSON PATH, INCLUDE_NULL_VALUES writes them, every column a key, null where it is null.";

        // A schema is only as true as what it assumed, so a declared session is said where the schema is read; with none
        // declared nothing is assumed, and nothing is said.
        if (source.Session.Count > 0)
        {
            var options = source.Session.Select(o => $"{o.Name} {(o.On ? "on" : "off")}");
            yield return $"Its types assume that callers' sessions set {Origins.Listed([.. options])}, as the config file declares.";
        }

        var schemas = definitions.SelectMany(d => Within(d.Schema)).ToList();
        var caveats = schemas.SelectMany(s => s.Notes).Select(n => n.Caveat).OfType<JsonSchemaCaveat>();

        // Every length is counted in code points, whether or not a note was needed to say how it was widened.
        if (schemas.Any(s => s.Keywords.ContainsKey(Keyword.MinLength) || s.Keywords.ContainsKey(Keyword.MaxLength)))
            caveats = caveats.Append(JsonSchemaCaveat.CodePoints);
        var listed = caveats.Distinct().Order().ToList();
        if (listed.Count == 0)
            yield break;
        yield return "The notes in descriptions rest on these facts:";
        foreach (var caveat in listed)
            yield return "- " + Caveat(caveat);
    }

    private static string Caveat(JsonSchemaCaveat caveat) => caveat switch
    {
        JsonSchemaCaveat.ByteLengths =>
            "varchar(n) and char(n) count bytes, not characters. A character is one byte only in a single-byte collation, so " +
            "elsewhere n bounds the length from above.",
        JsonSchemaCaveat.CodePoints =>
            "JSON Schema counts a string's length in code points, and SQL Server in UTF-16 code units or bytes, of which a code " +
            "point may take more than one, so a maximum length bounds the code points from above.",
        JsonSchemaCaveat.CollationValueSets =>
            "A value set from a CHECK constraint holds as the database compares strings, where case, accents and trailing " +
            "spaces may differ, so it is a note rather than an enum.",
        JsonSchemaCaveat.ContentSchemas =>
            "contentSchema says what a string of JSON parses to, but a validator checks it only when asked to: parse the " +
            "string and validate the value against it.",
        JsonSchemaCaveat.DecimalSteps =>
            "A decimal's places are not given as multipleOf, which a validator reading numbers as doubles tests inexactly: " +
            "0.07 divided by 0.01 is not a whole number in binary floating point.",
        JsonSchemaCaveat.Doubles =>
            "Many validators read a JSON number as a double, which holds every integer up to 2^53 exactly, and keeps the " +
            "digits of a decimal of up to 15 significant digits.",
        _ => throw new ArgumentOutOfRangeException(nameof(caveat), caveat, "A caveat outside the closed set."),
    };

    /// <summary>A schema and every schema inside it.</summary>
    private static IEnumerable<Schema> Within(Schema schema) =>
        schema.Keywords.Values.SelectMany(Inside).SelectMany(Within).Prepend(schema);

    private static IEnumerable<Schema> Inside(SchemaValue value) => value switch
    {
        SchemaValue.Nested nested => [nested.Schema],
        SchemaValue.List list => list.Items.SelectMany(Inside),
        SchemaValue.Properties properties => properties.Items.Select(p => p.Schema),
        _ => [],
    };

    /// <summary>
    /// A named object type: an object with a property per field, the keys that are always there <c>required</c>. Other keys are
    /// allowed by default, so a column the database adds before the application redeploys doesn't fail validation; strict objects
    /// forbid them (<c>docs/decisions/0008-objects-strip-unknown-keys-by-default.md</c>). Its description says where it comes from.
    /// </summary>
    /// <remarks>A property's description ends with why its field is what it is, as <c>"comments"</c> asks (<see cref="IrFieldText"/>).</remarks>
    private static Schema ObjectType(string name, IrObjectType type, JsonSchemaPlan plan, TypeLowering types)
    {
        var options = plan.Options;
        var place = Place.Of(type);
        var schema = Schema.OfType("object")
            .With(Keyword.Properties, new SchemaValue.Properties(
            [
                .. type.Fields.Select(f => new SchemaProperty(
                    f.Name,
                    types.Field(f, place).Noted(IrFieldText.Of(plan.Document, name, f, options.Comments).Select(line => new SchemaNote(line))))),
            ]));
        var required = type.Fields.Where(TypeLowering.IsRequired).Select(f => (SchemaValue)new SchemaValue.Text(f.Name)).ToList();
        if (required.Count > 0)
            schema = schema.With(Keyword.Required, new SchemaValue.List([.. required]));
        if (options.Objects == JsonSchemaObjects.Strict)
            schema = schema.With(Keyword.AdditionalProperties, new SchemaValue.Boolean(false));
        return type.Identity is { } identity ? schema.Noted(new SchemaNote($"The type at {identity.Path} in {identity.Object}.")) : schema;
    }

    /// <summary>
    /// A procedure: an object of its result sets by name when they are named, even one
    /// (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>); otherwise its one result's schema, an array
    /// of exactly as many as it returns, or <c>{}</c> when they can't be listed. Its title is its name in the database, and its
    /// description says what it returns.
    /// </summary>
    private static Schema Procedure(IrObject obj, TypeLowering types, UnknownDetails unknowns)
    {
        var results = new Place(obj.Id, [new IrPathKey("results")]);
        var schema = obj.Results switch
        {
            null => Schema.Any,
            [] => Schema.OfType("array").With(Keyword.MaxItems, 0),
            var sets when obj.SetNames is { } names => Schema.OfType("object")
                .With(Keyword.Properties, new SchemaValue.Properties([.. sets.Zip(names, (r, name) => (r, name)).Select((s, i) => new SchemaProperty(s.name, Result(s.r, results.Then(new IrPathIndex(i)), types)))]))
                .With(Keyword.Required, new SchemaValue.List([.. names.Select(name => (SchemaValue)new SchemaValue.Text(name))]))
                .With(Keyword.AdditionalProperties, new SchemaValue.Boolean(false)),
            [var single] => Result(single, results.Then(new IrPathIndex(0)), types),
            var many => Schema.OfType("array")
                .With(Keyword.PrefixItems, new SchemaValue.List([.. many.Select((r, i) => new SchemaValue.Nested(Result(r, results.Then(new IrPathIndex(i)), types)))]))
                .With(Keyword.Items, new SchemaValue.Boolean(false))
                .With(Keyword.MinItems, many.Count),
        };
        var summary = obj.Results switch
        {
            null => unknowns.Results(results),
            [] => new SchemaNote("Returns no result sets."),
            [{ Name: { } name } single] => new SchemaNote($"Returns one result set, {name}: {IrResultText.Describe(single)}."),
            [var single] => new SchemaNote($"Returns {IrResultText.Describe(single)}."),
            var many => new SchemaNote($"Returns {many.Count} result sets: {string.Join("; ", many.Select(r => (r.Name is { } name ? name + ": " : "") + IrResultText.Describe(r)))}."),
        };
        return schema.With(Keyword.Title, new SchemaValue.Text(obj.Id)) with { Notes = [summary, .. types.Declared(results), .. schema.Notes] };
    }

    /// <summary>
    /// A result's schema. A result that may be absent follows the reader convention in the IR's README: a rowset reads as
    /// <c>[]</c>, so its schema is unchanged, and a JSON result reads as <c>null</c>, whatever its type, because that is what the
    /// mssql driver hands over for a FOR JSON query that matched no rows
    /// (<c>docs/decisions/0048-an-absent-json-result-reads-as-null.md</c>). An array result is bounded as it is read
    /// (<see cref="IrResult.FewestItems"/>).
    /// </summary>
    private static Schema Result(IrResult result, Place place, TypeLowering types)
    {
        var schema = result.Type is IrArray array
            ? types.Type(array with { MinItems = null, MaxItems = null }, place).With(Keyword.MinItems, result.FewestItems).With(Keyword.MaxItems, result.MostItems)
            : types.Type(result.Type, place);
        return result.MayBeAbsent && result.Encoding == IrEncoding.Json
            ? schema.OrNull().Noted(Notes(Origins.Nulls("Null when absent", result.AbsentBecause)))
            : schema;
    }

    private static IEnumerable<SchemaNote> Notes(SchemaNote? note) => note is null ? [] : [note];
}
