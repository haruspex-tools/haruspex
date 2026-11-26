using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Haruspex.Ir;

/// <summary>
/// The JSON Schema of the IR's file, published as <see cref="PublishedPath"/> so that a program in any language
/// can check an IR file before it trusts one. Generated here, from the model, rather than kept by hand: every closed
/// vocabulary is listed from its enum through <see cref="IrWireNames"/>, so a member added to the model reaches the schema
/// with no one remembering to, and a test fails until the committed file is regenerated.
/// </summary>
/// <remarks>
/// <para>
/// What is written here by hand is the structure: which properties each object has and which are required, the same
/// structure <see cref="IrJsonWriter"/> writes and <see cref="IrJsonReader"/> reads. Four tests hold the three together:
/// every fixture's IR passes this schema, every file the writer writes reads back to the same bytes, every property of every
/// IR record is accounted for (<c>IrJsonSchemaTests</c>), and the schema and the reader refuse the same files, but for the
/// rules only the reader checks (<c>IrReaderSchemaAgreementTests</c>). A structure derived by reflection over the records would
/// have to restate the writer's choices (keys named otherwise than their properties, flags written only when true, the
/// facets beside the scalar's kind) as attributes on the model, a second copy of the writer that nothing would check.
/// </para>
/// <para>
/// The schema is strict where the format is closed: every object refuses properties it doesn't list, every vocabulary is an
/// <c>enum</c>, and a flag and its reasons must agree. It uses a small part of JSON Schema 2020-12 (<c>type</c>, <c>enum</c>,
/// <c>const</c>, <c>properties</c>, <c>required</c>, <c>dependentRequired</c>, <c>additionalProperties</c>, <c>items</c>,
/// <c>minItems</c>, <c>minProperties</c>, <c>maxProperties</c>, <c>minimum</c>, <c>maximum</c>, <c>minLength</c>, <c>pattern</c>,
/// <c>anyOf</c>, <c>not</c> and <c>$ref</c>), written so that Ajv's strict mode compiles it: every <c>properties</c>,
/// <c>required</c> and <c>items</c> beside the <c>type</c> it applies to, and every property required named in
/// <c>properties</c> too. <c>IrJsonSchemaTests</c> compiles it so, with the Ajv <c>tests/emit-check</c> pins.
/// What JSON Schema can't say (a number's form, a key given
/// twice, a name that names nothing or is given twice, and what a label says of the whole document) the reader checks alone: the IR
/// README lists it under "Rules only the reader checks", and <c>IrReaderSchemaAgreementTests</c> holds the two to that list.
/// </para>
/// </remarks>
public static class IrJsonSchema
{
    /// <summary>
    /// Where the current version's schema is kept in the repository, a file named for the version (<c>schemas/ir/0.30.schema.json</c>):
    /// one per version, so that a file is checked against what its version's reader reads, not against whatever is newest.
    /// Why: <c>schemas/ir/README.md</c>.
    /// </summary>
    public const string PublishedPath = "schemas/ir/" + IrDocument.CurrentVersion + ".schema.json";

    /// <summary>Where the schema is published, as its <c>$id</c>: <see cref="PublishedPath"/> on the site (<see cref="HaruspexSite.IrSchemas"/>), named for its version.</summary>
    public const string Id = HaruspexSite.IrSchemas + IrDocument.CurrentVersion + ".schema.json";

    /// <summary>The definitions of the type nodes, one per node of <see cref="IrType"/>'s closed set.</summary>
    private static readonly string[] TypeNodes = ["ref", "scalar", "array", "tuple", "record", "union", "jsonText", "anyJson", "import", "unknownType"];

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
        ("description", $"An IR file of version {IrDocument.CurrentVersion}, {IrDocument.FoldersVersion}, {IrDocument.BuildsVersion}, {IrDocument.StampedVersion}, {IrDocument.ExplainedVersion}, {IrDocument.DeclaredVersion}, {IrDocument.BytesVersion}, {IrDocument.TableTypesVersion}, {IrDocument.SourcesVersion}, {IrDocument.CombinedVersion} or {IrDocument.OneDatabaseVersion}: the shapes haruspex inferred for a set of database objects, of one database or (0.24) combining two, (0.25) where each field's value came from, (0.26) the table types its parameters take, (0.27) binary values, (0.28) the columns the config file declares to hold JSON of a schema, (0.29) what makes a reason so, in words, and a minimum length in code points, (0.30) the build that wrote it, (0.31) the packages the config file declared builds, (0.32) the folders of a source read from several, and (0.33) the database's descriptions of objects, parameters and fields. The format is described in src/Haruspex.Ir/README.md."),
        ("type", "object"),
        ("properties", Node(
            ("irVersion", Node(("description", $"The IR's version, which a reader checks before anything else, the oldest that says all the document says: {IrDocument.CurrentVersion} for a document that records a description (of an object, a parameter or a field, which this schema can't require), else {IrDocument.FoldersVersion} for one whose source was read from several folders (source.paths, or its baseline's), else {IrDocument.BuildsVersion} for one that records packages declared builds (source.builds, or its baseline's), else {IrDocument.StampedVersion} for one that names the build that wrote it (source.writtenBy), else {IrDocument.ExplainedVersion} for one in which a reason says what makes it so or a minimum length is in code points (a detail or a minimum's unit, which this schema can't require), else {IrDocument.DeclaredVersion} for one that declares a column's JSON (columns, or what only a declaration gives: any JSON, a fact from a declaration or an open object, which this schema can't require), else {IrDocument.BytesVersion} for one that holds a binary value (a scalar of kind bytes, which this schema can't require), else {IrDocument.TableTypesVersion} for one that defines a table type, else {IrDocument.SourcesVersion} for one that says where a field's value came from, else {IrDocument.CombinedVersion} for one combining two databases, else {IrDocument.OneDatabaseVersion}."), ("enum", Names(IrDocument.CurrentVersion, IrDocument.FoldersVersion, IrDocument.BuildsVersion, IrDocument.StampedVersion, IrDocument.ExplainedVersion, IrDocument.DeclaredVersion, IrDocument.BytesVersion, IrDocument.TableTypesVersion, IrDocument.SourcesVersion, IrDocument.CombinedVersion, IrDocument.OneDatabaseVersion)))),
            ("source", Source(
                "Where the shapes were inferred from.",
                ("baseline", Source("For a document combining two databases (0.24), where the other, the baseline, was read from. Always given when irVersion is 0.24, never when it is 0.23.")))),
            ("objects", ArrayOf(Ref("object"))),
            ("types", Node(
                ("description", "The named object types, by name."),
                ("type", "object"),
                ("additionalProperties", Ref("objectType")))),
            ("tableTypes", Node(
                ("description", "The table types the parameters take, by the key a parameter's tableType names (0.26). Always given when irVersion is 0.26, never before it, and when it is 0.27 exactly when a parameter takes a table type."),
                ("type", "object"),
                ("minProperties", 1),
                ("additionalProperties", Ref("tableType")))),
            ("columns", Node(
                ("description", "The columns the config file declares to hold JSON of a schema, sorted by column (0.28). Never given before 0.28."),
                ("type", "array"),
                ("minItems", 1),
                ("items", Ref("columnDeclaration")))),
            ("unknowns", ArrayOf(Ref("unknown"))),
            ("overrides", ArrayOf(Ref("override"))),
            ("folds", Node(
                ("description", "The folds the canonical value sets name, by name: how a collation compares a column's strings (0.20)."),
                ("type", "object"),
                ("additionalProperties", Ref("fold")))))),
        ("required", Names("irVersion", "source", "objects", "types", "unknowns", "overrides", "folds")),
        ("additionalProperties", false),

        // What each label allows, a branch for each version and, where it may be either, for a document of one database and one
        // combining two (Labels).
        ("anyOf", Labels()),
        ("$defs", Definitions()));

    /// <summary>
    /// What a document's label allows, as the reader checks it where JSON Schema can say it (decision 0072's labelling by content): a
    /// branch for each version, and for each of 0.25 and later one for a document of one database and one combining two. What the
    /// reader checks that no branch can say (that a document labelled 0.27 holds a binary value somewhere, for one) is listed in
    /// the IR README's "Rules only the reader checks".
    /// </summary>
    private static JsonArray Labels() =>
    [
        Label(IrDocument.OneDatabaseVersion, combined: false),
        Label(IrDocument.CombinedVersion, combined: true),
        .. ((string[])[IrDocument.SourcesVersion, IrDocument.TableTypesVersion, IrDocument.BytesVersion, IrDocument.DeclaredVersion, IrDocument.ExplainedVersion, IrDocument.StampedVersion, IrDocument.BuildsVersion, IrDocument.FoldersVersion, IrDocument.CurrentVersion])
            .SelectMany(version => (JsonNode[])[Label(version, combined: false), Label(version, combined: true)]),
    ];

    /// <summary>
    /// A document labelled <paramref name="version"/>, of one database or combining two. One labelled before 0.25 says nothing of
    /// where a value came from: no field has <c>from</c> or <c>sides</c>, and no source has <c>path</c> or <c>files</c>. One labelled
    /// before 0.26 defines no table type, and one labelled 0.26 always does. One labelled before 0.28 declares no column, has no open
    /// object type and makes no <c>column-json</c> claim. One of one database says nothing only a combined document says: no field has
    /// <c>sides</c>, and no reason is <c>not-in-both</c>; nor, before 0.28, is a field's presence <c>optional</c>, which from 0.28 a
    /// declared column's JSON says too.
    /// </summary>
    private static JsonObject Label(string version, bool combined)
    {
        var sources = IrDocument.IsSince(version, IrDocument.SourcesVersion);
        var declares = IrDocument.IsSince(version, IrDocument.DeclaredVersion);
        var writer = IrDocument.IsSince(version, IrDocument.StampedVersion);
        var mayDeclareBuilds = IrDocument.IsSince(version, IrDocument.BuildsVersion);
        var builds = version == IrDocument.BuildsVersion;
        var mayReadFolders = IrDocument.IsSince(version, IrDocument.FoldersVersion);
        var folders = version == IrDocument.FoldersVersion;
        var describes = version == IrDocument.CurrentVersion;
        var notInBoth = Node(("not", Node(("const", IrWireNames.Of(IrNullRule.NotInBoth)))));
        JsonObject Reasons() => Node(("type", "array"), ("items", Node(("type", "object"), ("properties", Node(("rule", notInBoth.DeepClone()))))));

        // A source of this document, its own or its baseline: before 0.25, without the path and files it was read from; before 0.30,
        // without the build that wrote it, which from 0.30 the document's own source always names; before 0.31, without packages
        // declared builds, which in 0.31 the document's own source or its baseline records; before 0.32, without several folders,
        // which in 0.32 the document's own source or its baseline records, and from 0.33 may.
        JsonObject Source(bool baseline)
        {
            string[] lacking = [.. sources ? [] : (string[])["path", "files"], .. writer ? [] : (string[])["writtenBy"], .. mayDeclareBuilds ? [] : (string[])["builds"], .. mayReadFolders ? [] : (string[])["paths"]];
            string[] absent = [.. baseline ? [] : (string[])["baseline"], .. lacking];
            var source = Node(("type", "object"));
            if (absent.Length > 0)
                source["not"] = AnyHaving(absent);
            var properties = Node();
            var required = new List<string>();
            if (baseline)
            {
                properties["baseline"] = lacking.Length > 0 ? Lacking(lacking) : true;
                required.Add("baseline");
            }

            if (writer)
            {
                properties["writtenBy"] = true;
                required.Add("writtenBy");
            }

            if (builds && baseline)
            {
                // Either source may hold them: the document's own, or its baseline's, read from a package declared a build.
                source["anyOf"] = new JsonArray(Having("builds"), Node(("type", "object"), ("properties", Node(("baseline", Having("builds"))))));
            }
            else if (builds)
            {
                properties["builds"] = true;
                required.Add("builds");
            }

            if (folders && baseline)
            {
                // Either source may have been read from several folders.
                source["anyOf"] = new JsonArray(Having("paths"), Node(("type", "object"), ("properties", Node(("baseline", Having("paths"))))));
            }
            else if (folders)
            {
                properties["paths"] = true;
                required.Add("paths");
            }

            if (properties.Count > 0)
                source["properties"] = properties;
            if (required.Count > 0)
                source["required"] = Names([.. required]);
            return source;
        }

        string[] notInField = [.. combined ? [] : (string[])["sides"], .. sources ? [] : (string[])["from", "sides"], .. describes ? [] : (string[])["description"]];
        var field = notInField.Length > 0 ? Lacking([.. notInField.Distinct()]) : Node(("type", "object"));
        if (!combined)
        {
            field["properties"] = declares
                ? Node(("nullableBecause", Reasons()))
                : Node(("nullableBecause", Reasons()), ("presence", Node(("not", Node(("const", IrWireNames.Of(IrPresence.Optional)))))));
        }

        var objectType = declares ? Node(("type", "object")) : Lacking("open");
        objectType["properties"] = Node(("fields", Node(("type", "array"), ("items", field))));
        var properties = Node(
            ("irVersion", Node(("const", version))),
            ("source", Source(combined)),
            ("types", Node(("type", "object"), ("additionalProperties", objectType))));
        // Before 0.33 no object or parameter has a description.
        var obj = describes ? Node(("type", "object")) : Lacking("description");
        var objectProperties = Node();
        if (!describes)
            objectProperties.Add("parameters", Node(("type", "array"), ("items", Lacking("description"))));
        if (!combined)
        {
            // Results are null when they can't be listed, so the rule holds of them only where they are an array.
            var result = Node(("type", "object"), ("properties", Node(("absentBecause", Reasons()))));
            objectProperties.Add("results", Node(("anyOf", new JsonArray(Node(("type", "null")), Node(("type", "array"), ("items", result))))));
        }

        if (objectProperties.Count > 0)
        {
            obj["properties"] = objectProperties;
            properties.Add("objects", Node(("type", "array"), ("items", obj)));
        }

        if (!declares)
        {
            var claim = Node(("type", "object"), ("properties", Node(("setting", Node(("not", Node(("const", IrWireNames.Of(IrOverrideSetting.ColumnJson)))))))));
            properties.Add("overrides", Node(("type", "array"), ("items", claim)));
        }

        var label = Node(("type", "object"), ("properties", properties));
        string[] absent = [.. IrDocument.IsSince(version, IrDocument.TableTypesVersion) ? [] : (string[])["tableTypes"], .. declares ? [] : (string[])["columns"]];
        if (version == IrDocument.TableTypesVersion)
        {
            properties.Add("tableTypes", true);
            label.Add("required", Names("tableTypes"));
        }

        if (absent.Length > 0)
            label.Add("not", AnyHaving(absent));
        return label;
    }

    /// <summary>A source, with the properties every source has and those given (a document's own source has its baseline).</summary>
    private static JsonObject Source(string description, params (string Name, JsonNode Schema)[] more) => Apart(["path", "paths"], Together(["package", "sha256"], Closed(
        description,
        ["dialect"],
        [
            ("dialect", Text("The SQL dialect, such as tsql.")),
            ("writtenBy", Ref("build")),
            ("path", Text("Where the source was read from, as the config file names it: relative to its folder, /-separated (0.25). Left out when not known, and for a source read from several folders.")),
            ("paths", Node(
                ("description", "For a source read from several folders of scripts as one database, the folders, each by its name as found on disk relative to the config file's folder, /-separated, as the locations name it, sorted (0.32). Every location into the source's scripts is then relative to the config file's folder, and so begins with the folder listed, rather than relative to one folder read. Left out for a source read from one folder, a project or a package, which path names."),
                ("type", "array"),
                ("minItems", 2),
                ("uniqueItems", true),
                ("items", Node(("type", "string"), ("minLength", 1))))),
            ("package", Text("The file name of the package the source was read from, such as a DACPAC. Left out for a source that is not a package.")),
            ("sha256", Sha256("The SHA-256 of the package's bytes. Given exactly when package is.")),
            ("references", Node(
                ("description", "The packages the package refers to, found or not, sorted by name and then database. Left out when there are none."),
                ("type", "array"),
                ("minItems", 1),
                ("items", Ref("packageReference")))),
            ("builds", Node(
                ("description", "The packages the config file declared builds, of those the source read: its package and the references found, by the names they give them, sorted (0.31). Their primary keys and unique constraints are claimed on the user's word. Left out when none is declared."),
                ("type", "array"),
                ("minItems", 1),
                ("uniqueItems", true),
                ("items", Text("A package's file name, as package or a reference names it.")))),
            ("session", Node(
                ("description", "The options the callers' sessions are declared to set, by the dialect's name for each: true for on, false for off. Left out when none is declared."),
                ("type", "object"),
                ("minProperties", 1),
                ("additionalProperties", Node(("type", "boolean"))))),
            ("files", Node(
                ("description", "The SHA-256 of each script file the document's locations point into, by its path as a location writes it (relative to the folder read, or to the config file's folder when paths is given), of its text with LF line endings in UTF-8 (0.25). Left out when there are none."),
                ("type", "object"),
                ("minProperties", 1),
                ("additionalProperties", Sha256("The file's SHA-256.")))),
            .. more,
        ])));

    private static JsonObject Definitions() => Node(
        ("build", Closed(
            "The build of the tool that wrote the source's claims (0.30). Always given for a document's own source when irVersion is 0.30 or later, never before it; a baseline's is left out when not known.",
            ["version"],
            ("version", Node(("description", "The tool's version, as it says it, without the commit."), ("type", "string"), ("minLength", 1))),
            ("commit", Node(("description", "The whole commit the build was made from; left out when the build didn't know it."), ("type", "string"), ("pattern", "^[0-9a-f]{40}([0-9a-f]{24})?$"))))),
        ("packageReference", Closed(
            "A package the source's package refers to.",
            ["package"],
            ("package", Text("Its file name, as the referring package names it.")),
            ("sha256", Sha256("The SHA-256 of the package read for it; left out when none was found.")),
            ("database", Text("Left out for the same database; otherwise the other database, by name or SQLCMD variable, as the reference names it.")))),
        ("object", RunsText(Closed(
            "A typed object: a procedure of the database, or a query file of the application.",
            ["id", "name", "kind", "at", "returnsCode", "parameters", "results"],
            ("id", Text("Its identity: a procedure's name in the database, or a query file's path relative to its config file's folder.")),
            ("name", Text("The name settled for generated code.")),
            ("kind", Vocabulary<IrObjectKind>(IrWireNames.Of)),
            ("at", Text("Where it is defined, as path:line:column.")),
            ("description", Description("The database's description of the procedure (0.33), from an extended property; left out when it has none.")),
            ("text", Text("What a query runs, as one batch, with LF line endings; only a query has it, and a query always does.")),
            ("returnsCode", Flag("Whether a call may end with a return code other than 0.")),
            ("parameters", ArrayOf(Ref("parameter"))),
            ("results", Node(
                ("description", "Its result sets in order, or null when they cannot be listed; named all or none."),
                ("anyOf", new JsonArray(
                    Node(("type", "null")),
                    ArrayOf(ResultNamed(true)),
                    ArrayOf(ResultNamed(false))))))))),
        ("parameter", Closed(
            "A declared parameter.",
            ["name", "type", "hasDefault", "output"],
            ("name", Text("Its name as declared, with its sigil.")),
            ("description", Description("The database's description of the parameter (0.33), from an extended property; left out when it has none.")),
            ("type", Ref("type")),
            ("tableType", Text("For a table-valued parameter (0.26), the key of its type's definition in tableTypes; left out for any other.")),
            ("hasDefault", Flag("Whether the caller may omit it.")),
            ("output", Flag("Whether it is an output parameter.")))),
        ("result", RowsetOfRows(Agreeing(
            Closed(
                "One result set.",
                ["encoding", "type", "mayBeAbsent", "cardinality"],
                ("name", SetName("The set's name, a key of the object its function returns; left out when the object's sets are unnamed, and given for all of them or none.")),
                ("encoding", Vocabulary<IrEncoding>(IrWireNames.Of)),
                ("type", Ref("type")),
                ("mayBeAbsent", Flag("Whether the result can arrive with no value at all.")),
                ("absentBecause", Ref("reasons")),
                ("cardinality", Vocabulary<IrCardinality>(IrWireNames.Of))),
            "mayBeAbsent",
            "absentBecause"))),
        ("rows", Node(
            ("description", "What a rowset's type is: one statement's rows, or a union of several statements' when paths differ."),
            ("anyOf", new JsonArray(Ref("statementRows"), OfKind("union", ("variants", Node(("type", "array"), ("items", Ref("statementRows"))))))))),
        ("statementRows", Node(
            ("description", "One statement's rows: an array of a named object type or of an unknown, an unknown, or what the config file declared them to be."),
            ("anyOf", new JsonArray(OfKind("array", ("items", Node(("anyOf", new JsonArray(Ref("ref"), Ref("unknownType")))))), Ref("unknownType"), Ref("declaredType"))))),
        ("declaredType", Node(
            ("description", "A type the config file declared, or arrays of one."),
            ("anyOf", new JsonArray(Ref("import"), OfKind("array", ("items", Ref("declaredType"))))))),
        ("objectType", Closed(
            "A named object type.",
            ["kind", "fields"],
            ("kind", Node(("const", "object"))),
            ("recursive", Flag("Whether the type refers to itself; left out when it doesn't.")),
            ("nameDeclared", Flag("Whether the config file gave the type its name; left out when the naming rules did.")),
            ("open", Flag("Whether its values may hold keys it doesn't list, of any value (0.28): JSON a column is declared to hold; left out when they hold only its fields' keys.")),
            ("identity", Ref("identity")),
            ("fields", ArrayOf(Ref("field"))))),
        ("identity", Identity()),
        ("tableType", Closed(
            "A table type's definition (0.26): what a table-valued parameter of it takes.",
            ["schema", "name", "at", "columns", "keys", "checks"],
            ("schema", Text("The schema it is defined in.")),
            ("name", Text("Its name within its schema.")),
            ("at", Text("Where it is defined, as path:line:column.")),
            ("columns", Node(
                ("description", "Its columns in the order declared, which is the order a client sends them in."),
                ("type", "array"),
                ("minItems", 1),
                ("items", Ref("tableColumn")))),
            ("keys", ArrayOf(Ref("tableKey"))),
            ("checks", Node(
                ("description", "Its CHECK constraints' conditions, each as written, on one line."),
                ("type", "array"),
                ("items", Node(("type", "string"))))))),
        ("tableColumn", TableColumn()),
        ("tableKey", Closed(
            "A key of a table type: columns whose values no two rows may share.",
            ["columns"],
            ("columns", Node(("type", "array"), ("minItems", 1), ("items", Node(("type", "string"))))),
            ("ignoresDuplicates", Flag("Whether a row whose key another has is dropped without an error; left out when false.")))),
        ("field", Agreeing(
            Closed(
                "A field of an object type.",
                ["name", "type", "presence", "nullable"],
                ("name", Text("The key or column name.")),
                ("description", Description("The database's description of the one column the field is, unchanged (0.33), from an extended property; left out when it has none, and for a field derived from several columns or an expression.")),
                ("type", Ref("type")),
                ("presence", Vocabulary<IrPresence>(IrWireNames.Of)),
                ("nullable", Flag("Whether the value can be null.")),
                ("nullableBecause", Ref("reasons")),
                ("from", Ref("from")),
                ("sides", Sides())),
            "nullable",
            "nullableBecause")),
        ("from", Node(
            ("description", "The table columns a value is read from, in the order the SQL first reads them (0.25). Left out when it reads none directly."),
            ("type", "array"),
            ("items", Ref("sourceColumn")),
            ("minItems", 1))),
        ("sourceColumn", Closed(
            "A table column a value is read from (0.25).",
            ["column", "at"],
            ("column", Text("The column, as schema.table.column.")),
            ("sql", Text("The column's type as a scalar's sql is written; left out when it is not a scalar the IR types.")),
            ("at", Text("Where the column is declared, as path:line:column.")))),
        ("side", Agreeing(
            Closed(
                "A field as one of the two combined databases gives it (0.25).",
                ["presence", "nullable"],
                ("presence", Vocabulary(IrWireNames.Of, IrPresence.Optional)),
                ("nullable", Flag("Whether that database's value can be null.")),
                ("nullableBecause", Ref("reasons")),
                ("sql", Text("The value's SQL type there, when it is a scalar.")),
                ("from", Ref("from"))),
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
            ("at", Text("What the rule applies to: a column or a source location.")),
            ("detail", Text("What makes it so, in words, when there is more to say than the rule (0.29): for a value read from JSON at a path, the steps that may find nothing or null.")))),
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
        ("minLength", Closed(
            "A string's minimum length, in UTF-16 code units, or (0.29) in code points when its unit says so.",
            ["value", "from"],
            ("value", Integer()),
            ("unit", Node(("description", "What value counts when not UTF-16 code units (0.29)."), ("const", IrWireNames.Of(IrLengthUnit.CodePoints)))),
            ("from", Ref("factSource")))),
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
        ("values", FoldAgreeing(Closed(
            "A set the value is drawn from.",
            ["match", "items", "from"],
            ("match", Vocabulary<IrValueMatch>(IrWireNames.Of)),
            ("items", Node(
                ("description", "The values: strings, or numbers in plain decimal form (no exponent), which JSON Schema can't check and the reader does."),
                ("type", "array"),
                ("items", Node(("anyOf", new JsonArray(Node(("type", "string")), Node(("type", "number")))))))),
            ("from", ArrayOf(Ref("factSource"))),
            ("fold", Text("The name of the fold in folds that gives a value's canonical form: present exactly when the match is canonical."))))),
        ("fold", Closed(
            "How a collation compares a column's strings. A string's canonical form drops each character of ignored, puts a class's name in place of each character of that class, keeps every other character, and then drops trailing spaces (U+0020), in that order.",
            ["ignored", "classes"],
            ("ignored", Text("The characters ignored, in ordinal order.")),
            ("classes", Node(
                ("description", "The characters of each class, in ordinal order, by the class's name, which is what they become."),
                ("type", "object"),
                ("additionalProperties", Node(("type", "string"), ("minLength", 1))))))),
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
        ("anyJson", Closed(
            "Any JSON value (0.28): an object, an array, a string, a number, a boolean or null, of any content.",
            ["kind"],
            ("kind", Node(("const", "any-json"))),
            ("objectOrArray", Flag("Whether it is only ever an object or an array; left out when it may be any JSON value.")))),
        ("columnDeclaration", Closed(
            "A column the config file declares to hold JSON of a schema (0.28).",
            ["column", "schema", "files"],
            ("column", Text("The column, as schema.table.column.")),
            ("schema", Text("The schema file, relative to the folder of the config file, /-separated.")),
            ("pointer", Text("The JSON Pointer to the schema used inside the file, when not the whole file.")),
            ("files", Node(
                ("description", "The SHA-256 of every schema file the declaration read, by its path, written as schema is."),
                ("type", "object"),
                ("minProperties", 1),
                ("additionalProperties", Node(("type", "string"), ("pattern", "^[0-9a-f]{64}$"))))))),
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
            ("via", Text("What the claim rests on, when not the object's own declaration: the procedure whose declared results it includes, or (0.28) the declared column it reads.")),
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

        // A binary value (0.27) has no format, and of the facets only a length, counted in bytes with no encoding.
        var bytes = IrWireNames.Of(IrScalarKind.Bytes);
        var unit = Node(("const", IrWireNames.Of(IrLengthUnit.Bytes)));
        JsonObject InBytes() => Node(("type", "object"), ("properties", Node(("unit", unit.DeepClone()))), ("not", Having("encoding")));
        string[] others = ["format", "minLength", "digits", "min", "max", "values"];
        var notBytes = Node(("properties", Node(("kind", Node(("not", Node(("const", bytes))))))));
        var othersAbsent = AnyHaving(others);
        var asBytes = Node(("type", "object"), ("not", othersAbsent), ("properties", Node(("length", InBytes()), ("maxLength", InBytes()))));
        scalar.Add("anyOf", new JsonArray(notBytes, asBytes));
        return scalar;
    }

    /// <summary>A table type's column, whose type a computed column alone may leave out.</summary>
    private static JsonObject TableColumn()
    {
        var column = Closed(
            "A column of a table type.",
            ["name", "nullable"],
            ("name", Text("Its name as declared.")),
            ("sql", Text("Its type as the database spells it; left out only for a computed column.")),
            ("type", Ref("scalar")),
            ("nullable", Flag("Whether a row may hold NULL in it.")),
            ("generated", Vocabulary<IrColumnGeneration>(IrWireNames.Of)),
            ("collation", Text("The collation its strings compare under, when known; left out otherwise.")));
        column.Add("anyOf", new JsonArray(
            Having("sql"),
            Node(("properties", Node(("generated", Node(("const", IrWireNames.Of(IrColumnGeneration.Computed)))))), ("required", Names("generated")))));
        return column;
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

    /// <summary>Holds a value set's fold to its match: named when the match is canonical, and left out otherwise.</summary>
    private static JsonObject FoldAgreeing(JsonObject schema)
    {
        // Each property named as well as required, as Having does, for a validator's strict mode.
        var canonicalMatch = Node(("const", IrWireNames.Of(IrValueMatch.Canonical)));
        var canonical = Node(("type", "object"), ("properties", Node(("match", canonicalMatch), ("fold", true))), ("required", Names("match", "fold")));
        var other = Node(("type", "object"), ("properties", Node(("match", Node(("not", canonicalMatch.DeepClone()))))), ("required", Names("match")), ("not", Having("fold")));
        schema.Add("anyOf", new JsonArray(canonical, other));
        return schema;
    }

    /// <summary>A result whose encoding is <c>rowset</c> has a type that is rows (<c>$defs/rows</c>): a driver hands one over as rows keyed by column.</summary>
    private static JsonObject RowsetOfRows(JsonObject schema)
    {
        var rowset = Node(("const", IrWireNames.Of(IrEncoding.Rowset)));
        schema.Add("not", Node(
            ("type", "object"),
            ("properties", Node(("encoding", rowset), ("type", Node(("not", Ref("rows")))))),
            ("required", Names("encoding", "type"))));
        return schema;
    }

    /// <summary>A type node of <paramref name="kind"/> whose <paramref name="property"/> is as given, whatever else it has.</summary>
    private static JsonObject OfKind(string kind, (string Name, JsonNode Schema) property) => Node(
        ("type", "object"),
        ("properties", Node(("kind", Node(("const", kind))), (property.Name, property.Schema))),
        ("required", Names("kind", property.Name)));

    /// <summary>A result that has a <c>name</c>, or one that has none: an object's sets are all of one or all of the other.</summary>
    private static JsonObject ResultNamed(bool named)
    {
        var schema = named ? Having("name") : Node(("not", Having("name")));
        schema.Add("$ref", "#/$defs/result");
        return schema;
    }

    /// <summary>An object's schema with <c>text</c> required of a query and refused on any other kind.</summary>
    private static JsonObject RunsText(JsonObject schema)
    {
        var kind = IrWireNames.Of(IrObjectKind.Query);
        var query = Node(
            ("type", "object"),
            ("properties", Node(("kind", Node(("const", kind))), ("text", true))),
            ("required", Names("kind", "text")));
        var other = Node(
            ("type", "object"),
            ("properties", Node(("kind", Node(("not", Node(("const", kind))))))),
            ("required", Names("kind")),
            ("not", Having("text")));
        schema.Add("anyOf", new JsonArray(query, other));
        return schema;
    }

    /// <summary>
    /// Holds a flag and its list of reasons to agree: true with the list, false (or, when <paramref name="flagOptional"/>,
    /// left out) without it.
    /// </summary>
    private static JsonObject Agreeing(JsonObject schema, string flag, string reasons, bool flagOptional = false)
    {
        var given = Having(flag, reasons);
        given["properties"]![flag] = Node(("const", true)); // Never null: Having always writes properties.
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

    /// <summary>An object that has none of the properties <paramref name="names"/>, whatever else it has.</summary>
    private static JsonObject Lacking(params string[] names) => Node(("type", "object"), ("not", AnyHaving(names)));

    /// <summary>An object that has at least one of the properties <paramref name="names"/>: for <c>not</c>.</summary>
    private static JsonObject AnyHaving(string[] names) =>
        names.Length == 1 ? Having(names) : Node(("type", "object"), ("anyOf", new JsonArray([.. names.Select(n => (JsonNode)Having(n))])));

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

    /// <summary>A combined field's sides (0.25): at least one, since a combined field comes from at least one database.</summary>
    private static JsonObject Sides()
    {
        var sides = Closed(
            "In a document combining two databases (0.25), the field as each gave it; a side is left out where that database doesn't return the key.",
            [],
            ("baseline", Ref("side")),
            ("candidate", Ref("side")));
        sides.Add("minProperties", 1);
        return sides;
    }

    private static JsonObject Text(string description) => Node(("description", description), ("type", "string"));

    /// <summary>A description from the database (0.33): text, never empty, since a thing with none leaves it out.</summary>
    private static JsonObject Description(string description) => Node(("description", description), ("type", "string"), ("minLength", 1));

    /// <summary>A set's name: an identifier, as <see cref="IrIdentifiers.IsIdentifier"/> reads one, and none of <see cref="IrResult.KeysBesideSets"/>.</summary>
    private static JsonObject SetName(string description) =>
        Node(
            ("description", description),
            ("type", "string"),
            ("pattern", "^[A-Za-z_$][A-Za-z0-9_$]*$"),
            ("not", Node(("enum", new JsonArray([.. IrResult.KeysBesideSets.Select(n => (JsonNode)n)])))));

    /// <summary>The object schema with each of <paramref name="names"/> required whenever any other of them is there.</summary>
    private static JsonObject Together(string[] names, JsonObject schema)
    {
        schema.Add("dependentRequired", Node([.. names.Select(name => (name, (JsonNode?)Names([.. names.Where(other => other != name)])))]));
        return schema;
    }

    /// <summary><paramref name="schema"/>, refusing an object that has both of the properties <paramref name="names"/>.</summary>
    private static JsonObject Apart(string[] names, JsonObject schema)
    {
        schema.Add("not", Having(names));
        return schema;
    }

    /// <summary>A SHA-256 as the IR writes it: 64 lower-case hexadecimal digits.</summary>
    private static JsonObject Sha256(string description) => Node(("description", description), ("type", "string"), ("pattern", "^[0-9a-f]{64}$"));

    private static JsonObject Flag(string description) => Node(("description", description), ("type", "boolean"));

    /// <summary>A count: every integer in the IR is one, from 0 to the largest a 32-bit integer holds.</summary>
    private static JsonObject Integer() => Node(("type", "integer"), ("minimum", 0), ("maximum", int.MaxValue));

    private static JsonObject ArrayOf(JsonNode items) => Node(("type", "array"), ("items", items));

    private static JsonObject Ref(string definition) => Node(("$ref", "#/$defs/" + definition));

    private static JsonArray Names(params string[] names) => new([.. names.Select(n => (JsonNode)n)]);

    private static JsonObject Node(params (string Key, JsonNode? Value)[] properties) =>
        new(properties.Select(p => KeyValuePair.Create(p.Key, p.Value)));
}
