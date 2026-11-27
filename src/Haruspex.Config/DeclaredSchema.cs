using System.Globalization;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Translates a JSON Schema the config file declares into the IR: the schema of one declared result, or of the JSON a column is
/// declared to hold. The subset read: <c>type</c> (one or several, with or without <c>"null"</c>), <c>properties</c> and
/// <c>required</c>, <c>items</c>, <c>minItems</c> and <c>maxItems</c>, <c>additionalProperties</c> for an object whose keys are
/// data, <c>format</c> for the kinds the IR has, <c>enum</c> and <c>const</c> of strings and numbers (a value set),
/// <c>minLength</c> and <c>maxLength</c>, <c>anyOf</c> and <c>oneOf</c> (a union), <c>$ref</c> to a JSON Pointer in the same document or in another
/// local file, and the schemas <c>{}</c> and <c>true</c>, any JSON. Annotations (<c>title</c>, <c>description</c>…) are ignored.
/// Any other keyword is a warning, and is not checked: the generated type is then wider than the schema, never narrower, so a
/// value the schema allows is never refused.
/// </summary>
/// <remarks>
/// <para>A declared scalar has no SQL type (<see cref="IrScalar.Sql"/> is null): <c>sql</c> is the database's type, which other
/// dialects and backends read, and a JSON Schema type is not one. Its <c>declared</c> null reason and fact source, and the claim
/// the IR lists, say where it came from.</para>
/// <para>Two keywords are read wider than they are written, on purpose. <c>oneOf</c> is read as <c>anyOf</c>: it also refuses a
/// value two branches accept, which a union doesn't check. A string's lengths count code points in JSON Schema: <c>maxLength</c>
/// is a maximum in code points, a unit of the IR's own, and <c>minLength</c> a minimum in UTF-16 code units, which n code points
/// are at least.</para>
/// A declaration is a claim, and is checked at runtime like the inferred types: the generated validator refuses a value that
/// does not fit it. So a wrong declaration fails loudly, on real data.
/// </remarks>
internal sealed class DeclaredSchema
{
    private static readonly HashSet<string> Annotations = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$anchor", "$comment", "$defs", "definitions", "title", "description", "examples", "default", "deprecated", "readOnly", "writeOnly",
    };

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        "type", "format", "properties", "required", "items", "additionalProperties", "minItems", "maxItems", "$ref", "enum", "const", "anyOf", "oneOf", "minLength", "maxLength",
    };

    private readonly SchemaDocument start;
    private readonly List<Diagnostic> problems;
    private readonly SchemaFiles files;
    private readonly string owner;
    private readonly bool column;
    private readonly Func<string, EquatableArray<IrField>, bool, string> define;

    /// <summary>The schema read, and its place: the start document's root, or a value a pointer names inside it.</summary>
    private readonly (JsonValue Value, string Path) top;

    /// <summary>The references being followed, innermost on top, each with the document its target is in.</summary>
    private readonly Stack<(string Target, SchemaDocument Document)> refs = new();

    /// <summary>The files read for this declaration, by full path: the one it starts in, and each a reference reached.</summary>
    private readonly SortedSet<string> read = new(StringComparer.Ordinal);

    private DeclaredSchema(SchemaDocument start, (JsonValue Value, string Path) top, List<Diagnostic> problems, SchemaFiles files, string owner, bool column, Func<string, EquatableArray<IrField>, bool, string> define)
    {
        this.start = start;
        this.top = top;
        this.problems = problems;
        this.files = files;
        this.owner = owner;
        this.column = column;
        this.define = define;
        read.Add(start.File);
    }

    /// <summary>The schema of a procedure's declared results, read from <paramref name="start"/>.</summary>
    /// <param name="start">Where the schema is: in place in the config file, or a file of its own.</param>
    /// <param name="problems">Where problems are reported.</param>
    /// <param name="files">The schema files read so far, which a reference to another file reads through.</param>
    /// <param name="procedure">The procedure declared, which owns every object type the schema defines.</param>
    /// <param name="types">Where each object type is defined, keyed by its procedure and path.</param>
    public static DeclaredSchema ForResults(SchemaDocument start, List<Diagnostic> problems, SchemaFiles files, string procedure, Dictionary<string, DeclaredType> types) =>
        new(start, (start.Root, start.RootPath), problems, files, procedure, column: false, (irPath, fields, _) =>
        {
            var key = Unique($"{procedure}:{irPath}", types.ContainsKey);
            types[key] = new DeclaredType(new IrTypeIdentity(IrTypeOwner.Procedure, procedure, irPath), fields);
            return key;
        });

    /// <summary>The schema of the JSON a column is declared to hold, read from <paramref name="start"/>.</summary>
    /// <param name="start">The schema's file, whose root a reference without a file refers into.</param>
    /// <param name="schema">The schema in it: its root, or the value a pointer names, with its place.</param>
    /// <param name="problems">Where problems are reported.</param>
    /// <param name="files">The schema files read so far, which a reference to another file reads through.</param>
    /// <param name="key">The column as the config file names it, <c>schema.table.column</c>.</param>
    /// <param name="types">Where each object type is defined, keyed by the column and path; no object owns them, since the
    /// inference reads them as a column's type, in every place its value reaches.</param>
    public static DeclaredSchema ForColumn(SchemaDocument start, (JsonValue Value, string Path) schema, List<Diagnostic> problems, SchemaFiles files, string key, Dictionary<string, IrObjectType> types) =>
        new(start, schema, problems, files, key, column: true, (irPath, fields, open) =>
        {
            var name = Unique($"{key}:{irPath}", types.ContainsKey);
            types[name] = new IrObjectType(fields) { Open = open };
            return name;
        });

    /// <summary>The files read for this declaration, by full path, in ordinal order.</summary>
    public IReadOnlyCollection<string> Files => read;

    private IrNullReason Declared => new(IrNullRule.Declared, owner);

    private IrFactSource Fact => new(IrFactSourceKind.Declared, owner);

    /// <summary>The document being read: the one a reference being followed is in, or the one the declaration starts in.</summary>
    private SchemaDocument Current => refs.Count > 0 ? refs.Peek().Document : start;

    /// <summary>A rowset: the schema of one row, an object whose properties are its columns, each always there.</summary>
    public IrResult? Rowset(int index)
    {
        var irPath = $"results[{index.ToString(CultureInfo.InvariantCulture)}][]";
        var pushed = refs.Count;
        try
        {
            return Row(start.Root, start.RootPath, irPath);
        }
        finally
        {
            while (refs.Count > pushed)
                refs.Pop();
        }
    }

    private IrResult? Row(JsonValue value, string at, string irPath)
    {
        // A row reached through $ref: its targets stay on the stack while its columns are read.
        while (value is JsonObjectValue withRef && Members(withRef).ContainsKey("$ref"))
        {
            if (Follow(withRef, at) is not var (definition, definitionPath))
                return null;
            (value, at) = (definition, definitionPath);
        }

        if (value is not JsonObjectValue schema || !Members(schema).ContainsKey("properties") || Types(schema, at) is not ["object"])
        {
            Report(ConfigCodes.RowsetShape, value, at, "a rowset is declared by the schema of one row: an object with \"properties\", one for each column.");
            return null;
        }

        Keywords(schema, at);
        var properties = Members(schema)["properties"];
        if (properties.Value is not JsonObjectValue columns)
        {
            Report(ConfigCodes.WrongKind, properties.Value, Child(at, "properties"), "must be an object: each column's name and schema.");
            return null;
        }

        if (Members(schema).TryGetValue("required", out var required)
            && (required.Value is not JsonArrayValue list || !columns.Members.All(c => list.Items.Any(i => i is JsonStringValue s && s.Value == c.Name))))
        {
            Report(ConfigCodes.RowsetRequired, required.Value, Child(at, "required"), "every column of a rowset is always there, so \"required\" must list them all, or be left out.");
            return null;
        }

        var fields = new List<IrField>();
        foreach (var rowColumn in columns.Members)
        {
            var columnPath = Child(Child(at, "properties"), rowColumn.Name);
            if (Node(rowColumn.Value, columnPath, irPath + IrPath.Key(rowColumn.Name)) is not var (type, nullable))
                continue;
            if (type is not IrScalar)
            {
                Report(ConfigCodes.RowsetColumn, rowColumn.Value, columnPath, "a rowset's column holds a scalar: a string, number, integer or boolean. JSON a column holds is a string.");
                continue;
            }

            fields.Add(new IrField(rowColumn.Name, type, IrPresence.Always, nullable ? [Declared] : []));
        }

        return new IrResult(IrEncoding.Rowset, new IrArray(new IrRef(define(irPath, [.. fields], false))), []);
    }

    /// <summary>A JSON result: the schema of the document FOR JSON writes, which may be left out when it matches no rows.</summary>
    public IrResult? Json(int index, bool mayBeAbsent)
    {
        var irPath = $"results[{index.ToString(CultureInfo.InvariantCulture)}]";
        if (Node(start.Root, start.RootPath, irPath) is not var (type, nullable))
            return null;
        if (nullable)
        {
            Report(ConfigCodes.NullDocument, start.Root, start.RootPath, "the JSON document itself is never null; when a query may match no rows, use \"mayBeAbsent\": true.");
            return null;
        }

        return new IrResult(IrEncoding.Json, type, mayBeAbsent ? [Declared] : []);
    }

    /// <summary>
    /// The JSON a column holds: the type its text parses to, or null when the schema can't be translated (the problem is
    /// reported). Its top can't be <c>null</c> but as any JSON: the IR's JSON text says what a non-null string parses to, and a
    /// column that holds no value is NULL in SQL, which is the column's own nullability.
    /// </summary>
    public IrType? Column()
    {
        var (root, rootPath) = top;
        if (Node(root, rootPath, "<json>") is not var (type, nullable))
            return null;
        if (nullable && type is not IrAnyJson)
        {
            Report(ConfigCodes.NullColumnJson, root, rootPath, "allows the JSON null at its top, which haruspex can't type in a column's text yet. A column with no value is NULL in SQL, which its own nullability says, so leave \"null\" out here.");
            return null;
        }

        return type;
    }

    /// <summary>
    /// The JSON a parameter is declared to hold, read as a column's is (<see cref="Column"/>): its top can't be <c>null</c> but as any
    /// JSON, since SQL Server's JSON functions refuse the text <c>null</c> (13609, a bare scalar), and a caller that has no value sends
    /// NULL, which the parameter's own nullability says.
    /// </summary>
    public IrType? Parameter()
    {
        var (root, rootPath) = top;
        if (Node(root, rootPath, "<json>") is not var (type, nullable))
            return null;
        if (nullable && type is not IrAnyJson)
        {
            Report(ConfigCodes.NullColumnJson, root, rootPath, "allows the JSON null at its top, which a parameter's JSON functions refuse (13609, as any bare scalar). A caller with no value sends NULL, which the parameter's own nullability says, so leave \"null\" out here.");
            return null;
        }

        return type;
    }

    /// <summary>
    /// The type of the body of an external REST endpoint's response, read as a column's JSON is (<see cref="Column"/>) but for its
    /// top: a body that is JSON <c>null</c> fails the call (11558), so <c>null</c> allowed there never arrives, and is left out.
    /// </summary>
    public IrType? Response()
    {
        var (root, rootPath) = top;
        return Node(root, rootPath, "<json>") is var (type, _) ? type : null;
    }

    /// <summary>A schema's type, and whether it may be null; null when it cannot be translated (the problem is reported).</summary>
    private (IrType Type, bool Nullable)? Node(JsonValue value, string at, string irPath)
    {
        switch (value)
        {
            // true allows every value, as {} does; false allows none, which no column or result can hold.
            case JsonOtherValue { Kind: System.Text.Json.JsonTokenType.True }:
                return (new IrAnyJson(), false);
            case JsonOtherValue { Kind: System.Text.Json.JsonTokenType.False }:
                Report(ConfigCodes.NoType, value, at, "is false, which allows no value at all.");
                return null;
            case JsonObjectValue withRef when Members(withRef).ContainsKey("$ref"):
                // The target stays on the stack while its definition is read, so a definition that refers to itself, at any
                // depth, is found rather than followed for ever, and a reference inside it is read in its own document.
                var pushed = refs.Count;
                try
                {
                    return Follow(withRef, at) is var (definition, definitionPath) ? Node(definition, definitionPath, irPath) : null;
                }
                finally
                {
                    while (refs.Count > pushed)
                        refs.Pop();
                }

            case JsonObjectValue schema:
                return Schema(schema, at, irPath);
            default:
                Report(ConfigCodes.NoType, value, at, "must be a schema: an object, or true.");
                return null;
        }
    }

    private (IrType Type, bool Nullable)? Schema(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);
        if (members.ContainsKey("anyOf") || members.ContainsKey("oneOf"))
            return Union(schema, at, irPath);
        if (members.ContainsKey("enum") || members.ContainsKey("const"))
            return Enum(schema, at, irPath);

        var types = Types(schema, at);
        if (types is null)
            return null;

        // No type is any JSON, as {} is; a keyword that constrains one type then constrains nothing the generated schema checks.
        if (types.Count == 0)
        {
            foreach (var member in schema.Members.Where(m => !Annotations.Contains(m.Name)))
                Report(ConfigCodes.UncheckedKeyword, member, Child(at, member.Name), "applies only to the type it is written for, and this schema gives no \"type\": the value may be any JSON, so the generated schema does not check it.");
            return (new IrAnyJson(), false);
        }

        var nullable = types.Contains("null");
        var nonNull = types.Where(t => t != "null").Distinct(StringComparer.Ordinal).ToList();
        if (nonNull.Count == 0)
        {
            Report(ConfigCodes.NoType, schema, at, "needs a type other than \"null\".");
            return null;
        }

        Keywords(schema, at);
        var variants = new List<IrType>();
        foreach (var type in nonNull)
        {
            // Each type takes the keywords written for it, as a validator applies them: properties to an object, items to an array.
            if (Typed(schema, type, at, irPath) is not { } typed)
                return null;
            variants.Add(typed);
        }

        return (Joined(variants), nullable);
    }

    /// <summary>A value of one JSON Schema type, with the keywords that apply to it.</summary>
    private IrType? Typed(JsonObjectValue schema, string type, string at, string irPath)
    {
        var members = Members(schema);
        var formatted = members.TryGetValue("format", out var f) && f.Value is JsonStringValue fs ? fs : null;
        var format = formatted?.Value;
        return type switch
        {
            "object" => ObjectNode(schema, at, irPath),
            "array" => ArrayNode(schema, at, irPath),
            "string" => format switch
            {
                null => Text(new IrScalar(IrScalarKind.String, null, null)),
                "date" => new IrScalar(IrScalarKind.Date, null, null),

                // What FOR JSON writes of SQL Server's types, which a declared result's writer is. An application may write any
                // form RFC 3339 allows, which a time's offset, a lower-case t or z, and a leap second take beyond the IR's
                // checks (observed in the pinned Zod: ZodObservationTests), so in a column's JSON they are strings.
                "time" or "date-time" or "date-time-local" when column => RfcString(),
                "time" => new IrScalar(IrScalarKind.Time, null, null),
                "date-time" => new IrScalar(IrScalarKind.Datetime, IrFormat.Offset, null),
                "date-time-local" => new IrScalar(IrScalarKind.Datetime, IrFormat.Local, null),
                "uuid" => new IrScalar(IrScalarKind.Uuid, null, null),
                _ => Unchecked(Text(new IrScalar(IrScalarKind.String, null, null))),
            },
            "integer" => format switch
            {
                null => new IrScalar(IrScalarKind.Number, null, null),
                "int32" => new IrScalar(IrScalarKind.Number, IrFormat.Int32, null),
                "int64" => new IrScalar(IrScalarKind.Number, IrFormat.Int64, null),
                _ => Unchecked(new IrScalar(IrScalarKind.Number, null, null)),
            },
            "number" => format is null ? new IrScalar(IrScalarKind.Number, null, null) : Unchecked(new IrScalar(IrScalarKind.Number, null, null)),
            "boolean" => format is null ? new IrScalar(IrScalarKind.Boolean, null, null) : Unchecked(new IrScalar(IrScalarKind.Boolean, null, null)),
            var other => Invalid(other),
        };

        IrScalar RfcString()
        {
            Report(ConfigCodes.UncheckedFormat, (JsonValue?)formatted ?? schema, Child(at, "format"), $"\"{format}\" allows forms an application may write (a time's offset, a lower-case t or z, a leap second) that haruspex's check of it would refuse, so in JSON an application writes it is a string and not checked.");
            return Text(new IrScalar(IrScalarKind.String, null, null));
        }

        IrScalar Unchecked(IrScalar scalar)
        {
            if (formatted is not null)
                Report(ConfigCodes.UncheckedFormat, formatted, Child(at, "format"), $"\"{format}\" is not a format haruspex checks, so the generated schema does not check it.");
            return scalar;
        }

        // A string's lengths, which JSON Schema counts in code points: its minimum is at least as many UTF-16 code units, which
        // the IR's minimum counts; its maximum is in code points, a unit of the IR's own (0.28), which every emitter's check counts
        // as JSON Schema does (observed in the pinned Zod: ZodObservationTests).
        IrScalar Text(IrScalar scalar)
        {
            var least = Count(members, "minLength", at);
            var most = Count(members, "maxLength", at);
            return least is null && most is null
                ? scalar
                : scalar with
                {
                    Facets = new IrFacets(
                        Length: most is { } m ? new IrLength(m, IsFixed: false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, Fact) : null,
                        MinLength: least is { } l ? new IrMinLength(l, Fact) { Unit = IrLengthUnit.CodePoints } : null),
                };
        }

        IrType? Invalid(string other)
        {
            Report(ConfigCodes.NotAType, members["type"].Value, Child(at, "type"), $"\"{other}\" is not a JSON Schema type: use string, integer, number, boolean, object, array or null.");
            return null;
        }
    }

    /// <summary>
    /// <c>anyOf</c> or <c>oneOf</c>: a value of any branch, null when a branch allows null. <c>oneOf</c> is read as <c>anyOf</c>,
    /// which also accepts a value two branches accept: wider, so never refusing what the schema allows. A keyword beside it is not
    /// checked, which is wider too.
    /// </summary>
    private (IrType Type, bool Nullable)? Union(JsonObjectValue schema, string at, string irPath)
    {
        var keyword = schema.Members.Any(m => m.Name == "anyOf") ? "anyOf" : "oneOf";
        foreach (var member in schema.Members.Where(m => !Annotations.Contains(m.Name) && m.Name != keyword))
            Report(ConfigCodes.UncheckedKeyword, member, Child(at, member.Name), $"is not checked beside \"{keyword}\", whose branches the generated schema checks alone.");
        var branches = Members(schema)[keyword];
        var branchesPath = Child(at, keyword);
        if (branches.Value is not JsonArrayValue { Items.Count: > 0 } list)
        {
            Report(ConfigCodes.WrongKind, branches.Value, branchesPath, "must be an array of at least one schema.");
            return null;
        }

        var variants = new List<IrType>();
        var nullable = false;
        for (var i = 0; i < list.Items.Count; i++)
        {
            var branch = list.Items[i];
            var branchPath = ConfigPath.Index(branchesPath, i);

            // A branch of null alone, as a nullable value's schema is often written, makes the union nullable and adds no variant.
            if (branch is JsonObjectValue only && Types(only, branchPath) is ["null"] && only.Members.All(m => m.Name == "type" || Annotations.Contains(m.Name)))
            {
                nullable = true;
                continue;
            }

            if (Node(branch, branchPath, irPath) is not var (type, branchNullable))
                return null;
            variants.Add(type);
            nullable |= branchNullable;
        }

        if (variants.Count == 0)
        {
            Report(ConfigCodes.NoType, list, branchesPath, "allows only null: give a branch a type other than \"null\".");
            return null;
        }

        return (Joined(variants), nullable);
    }

    /// <summary>
    /// <c>enum</c> or <c>const</c>: the strings and the numbers it lists, as a value set of each (null when it lists null), within
    /// the types <c>type</c> gives, if any. A list holding another kind of value is not checked, and the schema is read without it.
    /// </summary>
    private (IrType Type, bool Nullable)? Enum(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);
        var keyword = members.ContainsKey("enum") ? "enum" : "const";
        var listed = members[keyword];
        var items = keyword == "const" ? [listed.Value] : listed.Value is JsonArrayValue { Items.Count: > 0 } array ? [.. array.Items] : (List<JsonValue>?)null;
        if (items is null)
        {
            Report(ConfigCodes.WrongKind, listed.Value, Child(at, keyword), "must be an array of at least one value.");
            return null;
        }

        var types = Types(schema, at);
        if (types is null)
            return null;
        foreach (var member in schema.Members.Where(m => !Annotations.Contains(m.Name) && m.Name is not ("enum" or "const" or "type" or "format")))
            Report(ConfigCodes.UncheckedKeyword, member, Child(at, member.Name), $"is not checked beside \"{keyword}\", whose values the generated schema checks.");

        var strings = new List<IrValue>();
        var numbers = new List<IrValue>();
        var hasNull = false;
        foreach (var item in items)
        {
            switch (item)
            {
                case JsonStringValue text:
                    strings.Add(new IrValue(text.Value, IsString: true));
                    break;
                case JsonOtherValue { Kind: System.Text.Json.JsonTokenType.Number } number when IrJsonWriter.IsPlainDecimal(number.Text):
                    numbers.Add(new IrValue(number.Text, IsString: false));
                    break;
                case JsonOtherValue { Kind: System.Text.Json.JsonTokenType.Null }:
                    hasNull = true;
                    break;
                default:
                    Report(ConfigCodes.UncheckedKeyword, listed, Child(at, keyword), "lists a value that is not a string, a plain decimal number or null, so the generated schema does not check it.");
                    return Schema(Without(schema, keyword), at, irPath);
            }
        }

        // A value must have one of the types and be one of the values, so only the values of the types given count.
        bool Allows(params string[] kinds) => types.Count == 0 || kinds.Any(types.Contains);
        var variants = new List<IrType>();
        if (strings.Count > 0 && Allows("string"))
            variants.Add(new IrScalar(IrScalarKind.String, null, null, Values(strings)));
        if (numbers.Count > 0 && Allows("number", "integer"))
        {
            var int64 = members.TryGetValue("format", out var format) && format.Value is JsonStringValue { Value: "int64" } && types.Contains("integer");
            variants.Add(new IrScalar(IrScalarKind.Number, int64 ? IrFormat.Int64 : null, null, Values(numbers)));
        }

        if (variants.Count == 0)
        {
            Report(ConfigCodes.NoType, listed.Value, Child(at, keyword), "allows no value but null, or none of the type given.");
            return null;
        }

        return (Joined(variants), hasNull && Allows("null"));

        IrFacets Values(List<IrValue> values) => new(Values: new IrValueSet([.. values.Distinct()], IrValueMatch.Exact, [Fact]));
    }

    private IrType? ObjectNode(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);

        // patternProperties allows keys additionalProperties doesn't speak for, each of its own schema, which is not read: an
        // object with it holds keys of any JSON whatever additionalProperties says.
        var patterned = members.ContainsKey("patternProperties");
        if (!members.TryGetValue("properties", out var properties))
        {
            if (patterned)
                return new IrRecord(new IrAnyJson(), []);

            // An object whose keys are data: every value of one type, like what json_objectagg builds; or of any JSON, when the
            // schema says nothing of them, which is what an object of any keys holds.
            if (members.TryGetValue("additionalProperties", out var values) && values.Value is JsonObjectValue)
            {
                return Node(values.Value, Child(at, "additionalProperties"), irPath + "{}") is var (type, nullable)
                    ? new IrRecord(type, nullable ? [Declared] : [])
                    : null;
            }

            return values?.Value is JsonOtherValue { Kind: System.Text.Json.JsonTokenType.False }
                ? new IrRef(define(irPath, [], false))
                : new IrRecord(new IrAnyJson(), []);
        }

        if (properties.Value is not JsonObjectValue keys)
        {
            Report(ConfigCodes.WrongKind, properties.Value, Child(at, "properties"), "must be an object: each key's name and schema.");
            return null;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (members.TryGetValue("required", out var list))
        {
            if (list.Value is not JsonArrayValue array || array.Items.Any(i => i is not JsonStringValue))
            {
                Report(ConfigCodes.WrongKind, list.Value, Child(at, "required"), "must be an array of the keys that are always there.");
                return null;
            }

            foreach (var item in array.Items.Cast<JsonStringValue>())
            {
                if (!keys.Members.Any(k => k.Name == item.Value))
                    Report(ConfigCodes.RequiredUnknownKey, item, Child(at, "required"), $"names \"{item.Value}\", which is not in \"properties\".");
                required.Add(item.Value);
            }
        }

        // false closes the object; a schema beside "properties" is not read yet, so the keys it doesn't list may be any JSON.
        if (members.TryGetValue("additionalProperties", out var extra) && extra.Value is JsonObjectValue)
            Report(ConfigCodes.UncheckedKeyword, extra.Value, Child(at, "additionalProperties"), "is not supported beside \"properties\" yet, so the generated schema does not check it; the \"objects\" setting decides what happens to keys it does not list.");

        var fields = new List<IrField>();
        foreach (var key in keys.Members)
        {
            var keyPath = Child(Child(at, "properties"), key.Name);
            if (Node(key.Value, keyPath, irPath + IrPath.Key(key.Name)) is not var (type, nullable))
                continue;
            if (Field(key, keyPath, type, nullable, required.Contains(key.Name)) is { } field)
                fields.Add(field);
        }

        // A column's object may hold keys its schema doesn't list unless the schema closes it: whoever wrote it may have added
        // them. FOR JSON writes exactly a declared result's keys.
        var closed = !patterned && members.TryGetValue("additionalProperties", out var rest) && rest.Value is JsonOtherValue { Kind: System.Text.Json.JsonTokenType.False };
        return new IrRef(define(irPath, [.. fields], column && !closed));
    }

    /// <summary>
    /// A key of an object: present always when required, and null when its type allows null. Where else a key may be left out
    /// depends on who wrote the JSON. FOR JSON leaves a key out exactly when it is null, so in a declared result a key not
    /// required is never null, and one that may be both is an error. An application writing a column's JSON may leave a key out or
    /// write it null as it likes, so in a column's JSON a key not required may be left out whatever its value (0.28).
    /// </summary>
    private IrField? Field(JsonMember key, string keyPath, IrType type, bool nullable, bool required)
    {
        EquatableArray<IrNullReason> because = nullable ? [Declared] : [];
        if (required)
            return new IrField(key.Name, type, IrPresence.Always, because);
        if (column)
            return new IrField(key.Name, type, IrPresence.Optional, because);
        if (!nullable)
            return new IrField(key.Name, type, IrPresence.OmittedWhenNull, [Declared]);
        Report(ConfigCodes.OptionalAndNullable, key.Value, keyPath, "may be both left out and null, which haruspex can't express yet: FOR JSON leaves a null out, so make the key required, or not nullable.");
        return null;
    }

    private IrArray? ArrayNode(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);

        // An array that says nothing of its items holds any JSON. With prefixItems (a tuple, as Zod writes one), items speaks only
        // for the items after the prefix, so neither says what every item is: they are any JSON, and prefixItems is not checked
        // (its warning says so). items: false allows no item at all, so the array is empty.
        if (members.ContainsKey("prefixItems"))
            return Bounded(new IrArray(new IrAnyJson()));
        if (members.TryGetValue("items", out var none) && none.Value is JsonOtherValue { Kind: System.Text.Json.JsonTokenType.False })
            return new IrArray(new IrAnyJson()) { MaxItems = 0 };
        var (type, nullable) = members.TryGetValue("items", out var items)
            ? Node(items.Value, Child(at, "items"), irPath + "[]") is var (itemType, itemNullable) ? (itemType, itemNullable) : (null, false)
            : (new IrAnyJson(), false);
        if (type is null)
            return null;
        return Bounded(new IrArray(type) { ItemsNullableBecause = nullable ? [Declared] : [] });

        IrArray Bounded(IrArray array) => array with { MinItems = Count(members, "minItems", at), MaxItems = Count(members, "maxItems", at) };
    }

    /// <summary>
    /// The type of a value of any of <paramref name="variants"/>: any JSON when one is, which takes every value; the one when
    /// they are all one; else a union of them, each once.
    /// </summary>
    private static IrType Joined(List<IrType> variants)
    {
        var flat = variants.SelectMany(v => v is IrUnion union ? union.Variants : [v]).Distinct().ToList();
        if (flat.Any(v => v is IrAnyJson { ObjectOrArray: false }))
            return new IrAnyJson();
        return flat.Count == 1 ? flat[0] : new IrUnion([.. flat]);
    }

    private int? Count(Dictionary<string, JsonMember> members, string keyword, string at)
    {
        if (!members.TryGetValue(keyword, out var member))
            return null;
        if (member.Value is JsonOtherValue { Kind: System.Text.Json.JsonTokenType.Number } number
            && int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        Report(ConfigCodes.WrongKind, member.Value, Child(at, keyword), "must be a whole number, zero or more.");
        return null;
    }

    /// <summary>The types a schema allows, or null when <c>type</c> is malformed (reported). Empty when it has none.</summary>
    private List<string>? Types(JsonObjectValue schema, string at)
    {
        if (!Members(schema).TryGetValue("type", out var type))
            return [];
        switch (type.Value)
        {
            case JsonStringValue one:
                return [one.Value];
            case JsonArrayValue many when many.Items.All(i => i is JsonStringValue):
                return [.. many.Items.Cast<JsonStringValue>().Select(i => i.Value)];
            default:
                Report(ConfigCodes.WrongKind, type.Value, Child(at, "type"), "must be a type's name, or an array of them.");
                return null;
        }
    }

    /// <summary>Warns of each keyword that is neither read nor an annotation: it is not checked.</summary>
    private void Keywords(JsonObjectValue schema, string at)
    {
        foreach (var member in schema.Members)
        {
            if (Annotations.Contains(member.Name) || Read.Contains(member.Name))
                continue;
            Report(ConfigCodes.UncheckedKeyword, member, Child(at, member.Name), "is not supported yet, so the generated schema does not check it.");
        }
    }

    /// <summary>
    /// The value a <c>$ref</c> names, its target pushed on the stack of references with the document it is in; null when it names
    /// none, one already on the stack, or one outside the local files (reported). A reference is a URI: a file, relative to the
    /// document it is written in, and a JSON Pointer fragment; without a file, the same document; without a fragment, the whole file.
    /// </summary>
    private (JsonValue Value, string Path)? Follow(JsonObjectValue schema, string path)
    {
        var reference = Members(schema)["$ref"];
        var refPath = Child(path, "$ref");
        if (reference.Value is not JsonStringValue { Value: var target })
        {
            Report(ConfigCodes.WrongKind, reference.Value, refPath, "must be a reference: a file, a JSON Pointer after #, or both.");
            return null;
        }

        foreach (var sibling in schema.Members.Where(m => m.Name != "$ref" && !Annotations.Contains(m.Name)))
            Report(ConfigCodes.IgnoredBesideReference, sibling, Child(path, sibling.Name), "is ignored beside \"$ref\".");

        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var location = hash < 0 ? target : target[..hash];
        var fragment = hash < 0 ? "" : target[(hash + 1)..];
        if (IsRemote(location))
        {
            Report(ConfigCodes.ForeignReference, reference.Value, refPath, Remote(location));
            return null;
        }

        var document = Current;
        if (location.Length > 0)
        {
            // Relative to the folder of the document it is written in: a schema file's, or the config file's.
            var file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document.File) ?? "", Uri.UnescapeDataString(location)));
            if (files.Load(file, new JsonLocation(document.File, refPath, reference.Value.Line, reference.Value.Column), location) is not { } loaded)
                return null;
            read.Add(file);
            document = loaded;
        }

        if (JsonPointer.Resolve(document.Root, document.RootPath, fragment) is not var (value, valuePath))
        {
            Report(ConfigCodes.MissingReference, reference.Value, refPath, $"refers to \"{target}\", which names nothing: a fragment is a JSON Pointer, such as #/$defs/person.");
            return null;
        }

        var key = document.File + "\n" + document.RootPath + "#" + fragment;
        if (refs.Any(r => r.Target == key))
        {
            Report(ConfigCodes.RecursiveSchema, reference.Value, refPath, $"refers to \"{target}\" inside itself: a recursive schema is not supported yet.");
            return null;
        }

        refs.Push((key, document));
        return (value, valuePath);
    }

    /// <summary>Whether a schema's location names something other than a local file: a URL of the network.</summary>
    public static bool IsRemote(string location) =>
        Uri.TryCreate(location, UriKind.Absolute, out var absolute) && absolute.Scheme != Uri.UriSchemeFile && !Path.IsPathRooted(location);

    /// <summary>Why a schema on the network is refused, whether a <c>$ref</c> or a column's <c>json</c> names it.</summary>
    public static string Remote(string location) =>
        $"refers to {location}, which is not a local file: haruspex never fetches a schema, so keep a copy beside this one and refer to it.";

    /// <summary>The schema without one keyword, read again as if it were not written.</summary>
    private static JsonObjectValue Without(JsonObjectValue schema, string keyword) =>
        schema with { Members = [.. schema.Members.Where(m => m.Name != keyword)] };

    private static string Unique(string key, Func<string, bool> taken)
    {
        var candidate = key;
        for (var n = 2; taken(candidate); n++)
            candidate = key + "~" + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static Dictionary<string, JsonMember> Members(JsonObjectValue schema)
    {
        var members = new Dictionary<string, JsonMember>(StringComparer.Ordinal);
        foreach (var member in schema.Members)
            members[member.Name] = member;
        return members;
    }

    private void Report(DiagnosticCode code, JsonValue value, string path, string message) =>
        problems.Add(code.At(new JsonLocation(Current.File, path, value.Line, value.Column), message));

    private void Report(DiagnosticCode code, JsonMember member, string path, string message) =>
        problems.Add(code.At(new JsonLocation(Current.File, path, member.Line, member.Column), message));

    private static string Child(string path, string key) => ConfigPath.Child(path, key);
}
