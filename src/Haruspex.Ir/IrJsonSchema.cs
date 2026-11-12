using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Haruspex.Ir;

/// <summary>
/// The JSON Schema of the IR's file, published as <c>schemas/haruspex.ir.schema.json</c> so that a program in any language
/// can check an IR file before it trusts one. Generated here, from the model, rather than kept by hand: every closed
/// vocabulary is listed from its enum through <see cref="IrWireNames"/>, so a member added to the model reaches the schema
/// with no one remembering to, and a test fails until the committed file is regenerated.
/// </summary>
/// <remarks>
/// <para>
/// What is written here by hand is the structure: which properties each object has and which are required, the same
/// structure <see cref="IrJsonWriter"/> writes and <see cref="IrJsonReader"/> reads. Three tests hold the three together:
/// every fixture's IR passes this schema, every file the writer writes reads back to the same bytes, and every property of
/// every IR record is accounted for (<c>IrJsonSchemaTests</c>). A structure derived by reflection over the records would
/// have to restate the writer's choices (keys named otherwise than their properties, flags written only when true, the
/// facets beside the scalar's kind) as attributes on the model, a second copy of the writer that nothing would check.
/// </para>
/// <para>
/// The schema is strict where the format is closed: every object refuses properties it doesn't list, every vocabulary is an
/// <c>enum</c>, and a flag and its reasons must agree. It uses a small part of JSON Schema 2020-12 (<c>type</c>, <c>enum</c>,
/// <c>const</c>, <c>properties</c>, <c>required</c>, <c>additionalProperties</c>, <c>items</c>, <c>minItems</c>,
/// <c>maxProperties</c>, <c>minimum</c>, <c>maximum</c>, <c>anyOf</c>, <c>not</c> and <c>$ref</c>), written so that Ajv's strict
/// mode compiles it too. Three things JSON Schema can't say: a number's form (a bound or a value set's number must be plain
/// decimal text, with no exponent) and that no key is given twice, since it sees a document after parsing; and that a
/// <c>ref</c> names a type in <c>types</c>, since it can't compare a value with a key elsewhere. The reader checks all three;
/// the schema's descriptions state the first.
/// </para>
/// </remarks>
public static class IrJsonSchema
{
    /// <summary>Where the schema is published, as its <c>$id</c>.</summary>
    public const string Id = "https://github.com/sharpblue/haruspex/raw/main/schemas/haruspex.ir.schema.json";

    /// <summary>The definitions of the type nodes, one per node of <see cref="IrType"/>'s closed set.</summary>
    private static readonly string[] TypeNodes = ["ref", "scalar", "array", "tuple", "record", "union", "jsonText", "import", "unknownType"];

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The schema's text: the same model always gives the same bytes.</summary>
    public static string Write()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Options))
            Schema().WriteTo(json);
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static JsonObject Schema() => Node(
        ("$schema", "https://json-schema.org/draft/2020-12/schema"),
        ("$id", Id),
        ("title", "haruspex.ir.json"),
        ("description", $"An IR file of version {IrDocument.CurrentVersion}: the shapes haruspex inferred for a set of database objects. The format is described in src/Haruspex.Ir/README.md."),
        ("type", "object"),
        ("properties", Node(
            ("irVersion", Node(("description", "The IR's version, which a reader checks before anything else."), ("const", IrDocument.CurrentVersion))),
            ("source", Closed("Where the shapes were inferred from.", ["dialect"], ("dialect", Text("The SQL dialect, such as tsql.")))),
            ("objects", ArrayOf(Ref("object"))),
            ("types", Node(
                ("description", "The named object types, by name."),
                ("type", "object"),
                ("additionalProperties", Ref("objectType")))),
            ("unknowns", ArrayOf(Ref("unknown"))),
            ("overrides", ArrayOf(Ref("override"))))),
        ("required", Names("irVersion", "source", "objects", "types", "unknowns", "overrides")),
        ("additionalProperties", false),
        ("$defs", Definitions()));

    private static JsonObject Definitions() => Node(
        ("object", Closed(
            "A typed database object.",
            ["id", "name", "kind", "at", "parameters", "results"],
            ("id", Text("Its name in the database: its identity.")),
            ("name", Text("The name settled for generated code.")),
            ("kind", Vocabulary<IrObjectKind>(IrWireNames.Of)),
            ("at", Text("Where it is defined, as path:line:column.")),
            ("parameters", ArrayOf(Ref("parameter"))),
            ("results", Node(
                ("description", "Its result sets in order, or null when they cannot be listed."),
                ("anyOf", new JsonArray(Node(("type", "null")), ArrayOf(Ref("result")))))))),
        ("parameter", Closed(
            "A declared parameter.",
            ["name", "type", "hasDefault", "output"],
            ("name", Text("Its name as declared, with its sigil.")),
            ("type", Ref("type")),
            ("hasDefault", Flag("Whether the caller may omit it.")),
            ("output", Flag("Whether it is an output parameter.")))),
        ("result", Agreeing(
            Closed(
                "One result set.",
                ["encoding", "type", "mayBeAbsent", "cardinality"],
                ("encoding", Vocabulary<IrEncoding>(IrWireNames.Of)),
                ("type", Ref("type")),
                ("mayBeAbsent", Flag("Whether the result can arrive with no value at all.")),
                ("absentBecause", Ref("reasons")),
                ("cardinality", Vocabulary<IrCardinality>(IrWireNames.Of))),
            "mayBeAbsent",
            "absentBecause")),
        ("objectType", Closed(
            "A named object type.",
            ["kind", "fields"],
            ("kind", Node(("const", "object"))),
            ("recursive", Flag("Whether the type refers to itself; left out when it doesn't.")),
            ("identity", Ref("identity")),
            ("fields", ArrayOf(Ref("field"))))),
        ("identity", Identity()),
        ("field", Agreeing(
            Closed(
                "A field of an object type.",
                ["name", "type", "presence", "nullable"],
                ("name", Text("The key or column name.")),
                ("type", Ref("type")),
                ("presence", Vocabulary<IrPresence>(IrWireNames.Of)),
                ("nullable", Flag("Whether the value can be null.")),
                ("nullableBecause", Ref("reasons"))),
            "nullable",
            "nullableBecause")),
        ("reasons", Node(
            ("description", "Why a value can be null, or a result absent: at least one reason."),
            ("type", "array"),
            ("items", Ref("reason")),
            ("minItems", 1))),
        ("reason", Closed(
            "One reason a value can be null.",
            ["rule", "at"],
            ("rule", Vocabulary<IrNullRule>(IrWireNames.Of)),
            ("at", Text("What the rule applies to: a column or a source location.")))),
        ("type", Node(
            ("description", "A type node: a reference to a named type, or a kind."),
            ("anyOf", new JsonArray([.. TypeNodes.Select(Ref)])))),
        ("ref", Closed("A reference to a named object type.", ["ref"], ("ref", Text("The type's name in types.")))),
        ("scalar", Scalar()),
        ("length", Closed(
            "A string's length: a maximum (maxLength) or a fixed length (length).",
            ["value", "unit", "from"],
            ("value", Integer()),
            ("unit", Vocabulary<IrLengthUnit>(IrWireNames.Of)),
            ("encoding", Vocabulary(IrWireNames.Of, IrByteEncoding.Unknown)),
            ("from", Ref("factSource")))),
        ("minLength", Closed("A string's minimum length in UTF-16 code units.", ["value", "from"], ("value", Integer()), ("from", Ref("factSource")))),
        ("digits", Closed(
            "A decimal's precision and scale.",
            ["precision", "scale", "from"],
            ("precision", Integer()),
            ("scale", Integer()),
            ("from", Ref("factSource")))),
        ("bound", Closed(
            "A bound on a number: exact decimal text written as a JSON number, in plain form (no exponent), which a reader must not round. JSON Schema can't check a number's form; the reader refuses any other.",
            ["value", "inclusive", "from"],
            ("value", Node(("type", "number"))),
            ("inclusive", Flag("Whether the bound itself is allowed.")),
            ("from", Ref("factSource")))),
        ("values", Closed(
            "A set the value is drawn from.",
            ["match", "items", "from"],
            ("match", Vocabulary<IrValueMatch>(IrWireNames.Of)),
            ("items", Node(
                ("description", "The values: strings, or numbers in plain decimal form (no exponent), which JSON Schema can't check and the reader does."),
                ("type", "array"),
                ("items", Node(("anyOf", new JsonArray(Node(("type", "string")), Node(("type", "number")))))))),
            ("from", ArrayOf(Ref("factSource"))))),
        ("factSource", FactSource()),
        ("array", Agreeing(
            Closed(
                "An array of items.",
                ["kind", "items"],
                ("kind", Node(("const", "array"))),
                ("items", Ref("type")),
                ("itemsNullable", Flag("Whether an item can be null; left out when none can.")),
                ("itemsNullableBecause", Ref("reasons")),
                ("minItems", Integer()),
                ("maxItems", Integer())),
            "itemsNullable",
            "itemsNullableBecause",
            flagOptional: true)),
        ("tuple", Closed(
            "An array of a fixed length whose every position has its own type.",
            ["kind", "items"],
            ("kind", Node(("const", "tuple"))),
            ("items", ArrayOf(Ref("element"))))),
        ("element", Agreeing(
            Closed(
                "One position of a tuple.",
                ["type", "nullable"],
                ("type", Ref("type")),
                ("nullable", Flag("Whether the element can be null.")),
                ("nullableBecause", Ref("reasons"))),
            "nullable",
            "nullableBecause")),
        ("record", Agreeing(
            Closed(
                "An object whose keys are data, every value of one type.",
                ["kind", "values", "valuesNullable"],
                ("kind", Node(("const", "record"))),
                ("values", Ref("type")),
                ("valuesNullable", Flag("Whether a value can be null.")),
                ("valuesNullableBecause", Ref("reasons"))),
            "valuesNullable",
            "valuesNullableBecause")),
        ("union", Closed(
            "A value that has one of several types.",
            ["kind", "variants"],
            ("kind", Node(("const", "union"))),
            ("variants", Node(("type", "array"), ("items", Ref("type")), ("minItems", 2))))),
        ("jsonText", Closed(
            "A string that holds JSON, with what it parses to when that is known.",
            ["kind"],
            ("kind", Node(("const", "json-text"))),
            ("content", Ref("type")))),
        ("import", Closed(
            "A schema the application already has, which the config file declared.",
            ["kind", "name", "from"],
            ("kind", Node(("const", "import"))),
            ("name", Text("The exported name.")),
            ("from", Text("The module it is exported by.")))),
        ("unknownType", Closed(
            "A value whose type is not known.",
            ["kind", "reason"],
            ("kind", Node(("const", "unknown"))),
            ("reason", Vocabulary<IrUnknownReason>(IrWireNames.Of)))),
        ("unknown", Closed(
            "Something that could not be typed.",
            ["object", "path", "reason", "detail", "at"],
            ("object", Text("The id of the object it belongs to.")),
            ("path", Text("Where in the object, in the IR's path syntax.")),
            ("reason", Vocabulary<IrUnknownReason>(IrWireNames.Of)),
            ("detail", Text("A human-readable explanation.")),
            ("at", Text("The source location of the construct.")))),
        ("override", Closed(
            "A claim the config file made rather than inference.",
            ["object", "path", "setting"],
            ("object", Text("The id of the object it applies to.")),
            ("path", Text("Where in the object, in the IR's path syntax.")),
            ("setting", Vocabulary<IrOverrideSetting>(IrWireNames.Of)),
            ("via", Text("The procedure whose declaration the claim rests on, when not the object itself.")),
            ("why", Text("The reason given in the config file.")))));

    /// <summary>A scalar: its kind and format, its SQL type, and its facets beside them.</summary>
    private static JsonObject Scalar()
    {
        var scalar = Closed(
            "A scalar with its logical kind, the SQL type it came from, and its facets.",
            ["kind"],
            ("kind", Vocabulary<IrScalarKind>(IrWireNames.Of)),
            ("format", Vocabulary<IrFormat>(IrWireNames.Of)),
            ("sql", Text("The source's own type, as written.")),
            ("length", Ref("length")),
            ("maxLength", Ref("length")),
            ("minLength", Ref("minLength")),
            ("digits", Ref("digits")),
            ("min", Ref("bound")),
            ("max", Ref("bound")),
            ("values", Ref("values")));

        // A length is fixed or a maximum, never both.
        scalar.Add("not", Having("length", "maxLength"));
        return scalar;
    }

    /// <summary>A type's identity: one owner, keyed by its kind, beside the path.</summary>
    private static JsonObject Identity()
    {
        var owners = Enum.GetValues<IrTypeOwner>().Select(IrWireNames.Of).ToList();
        var identity = Closed(
            "Where a type comes from: the database object it belongs to, keyed by the object's kind, and the path to it there.",
            ["path"],
            [.. owners.Select(o => (o, (JsonNode)Text($"The {o} the type belongs to."))), ("path", Text("The path to the type, in the IR's path syntax."))]);
        identity.Add("maxProperties", 2);
        identity.Add("anyOf", new JsonArray([.. owners.Select(o => Having(o))]));
        return identity;
    }

    /// <summary>Where a fact came from: one property, named for the kind of source, holding its ref.</summary>
    private static JsonObject FactSource()
    {
        var kinds = Enum.GetValues<IrFactSourceKind>().Select(IrWireNames.Of).ToList();
        var source = Closed(
            "Where a fact came from: one property, named for the kind of source, whose value is the type's or constraint's name or a location.",
            [],
            [.. kinds.Select(k => (k, (JsonNode)Text($"The {k} the fact came from.")))]);
        source.Add("maxProperties", 1);
        source.Add("anyOf", new JsonArray([.. kinds.Select(k => Having(k))]));
        return source;
    }

    /// <summary>
    /// Holds a flag and its list of reasons to agree: true with the list, false (or, when <paramref name="flagOptional"/>,
    /// left out) without it.
    /// </summary>
    private static JsonObject Agreeing(JsonObject schema, string flag, string reasons, bool flagOptional = false)
    {
        var given = Having(flag, reasons);
        given["properties"]![flag] = Node(("const", true));
        var without = flagOptional ? Node(("type", "object")) : Having(flag);
        without["properties"] = Node((flag, Node(("const", false))));
        without.Add("not", Having(reasons));
        schema.Add("anyOf", new JsonArray(given, without));
        return schema;
    }

    /// <summary>
    /// An object that has the properties <paramref name="names"/>, whatever their values: for <c>anyOf</c> and <c>not</c>. The
    /// properties are named as well as required, so that a validator's strict mode (Ajv's <c>strictRequired</c>) finds each
    /// defined where it is required.
    /// </summary>
    private static JsonObject Having(params string[] names) => Node(
        ("type", "object"),
        ("properties", Node([.. names.Select(n => (n, (JsonNode?)true))])),
        ("required", Names(names)));

    /// <summary>An object that refuses properties it doesn't list.</summary>
    private static JsonObject Closed(string description, string[] required, params (string Name, JsonNode Schema)[] properties)
    {
        var schema = Node(
            ("description", description),
            ("type", "object"),
            ("properties", Node([.. properties.Select(p => (p.Name, (JsonNode?)p.Schema))])));
        if (required.Length > 0)
            schema.Add("required", Names(required));
        schema.Add("additionalProperties", false);
        return schema;
    }

    /// <summary>The names of a closed vocabulary, from its enum through its wire-name table, in the enum's order.</summary>
    private static JsonObject Vocabulary<T>(Func<T, string> name, params T[] unnamed)
        where T : struct, Enum =>
        Node(("enum", new JsonArray([.. Enum.GetValues<T>().Where(v => !unnamed.Contains(v)).Select(v => (JsonNode)name(v))])));

    private static JsonObject Text(string description) => Node(("description", description), ("type", "string"));

    private static JsonObject Flag(string description) => Node(("description", description), ("type", "boolean"));

    /// <summary>A count: every integer in the IR is one, from 0 to the largest a 32-bit integer holds.</summary>
    private static JsonObject Integer() => Node(("type", "integer"), ("minimum", 0), ("maximum", int.MaxValue));

    private static JsonObject ArrayOf(JsonNode items) => Node(("type", "array"), ("items", items));

    private static JsonObject Ref(string definition) => Node(("$ref", "#/$defs/" + definition));

    private static JsonArray Names(params string[] names) => new([.. names.Select(n => (JsonNode)n)]);

    private static JsonObject Node(params (string Key, JsonNode? Value)[] properties) =>
        new(properties.Select(p => KeyValuePair.Create(p.Key, p.Value)));
}
