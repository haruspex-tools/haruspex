using System.Globalization;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Translates the JSON Schema of one declared result into the IR. The subset read is the one a result of SQL Server can
/// have: <c>type</c> (one type, with or without <c>"null"</c>), <c>properties</c> and <c>required</c>, <c>items</c>,
/// <c>minItems</c> and <c>maxItems</c>, <c>additionalProperties</c> for an object whose keys are data, <c>format</c> for the
/// kinds the IR has, and <c>$ref</c> to <c>$defs</c> in the same schema. Annotations (<c>title</c>, <c>description</c>…)
/// are ignored. Any other keyword is a warning, and is not checked: the generated type is then wider than the schema,
/// never narrower, so a value the schema allows is never refused.
/// </summary>
/// <remarks>
/// <para>A declared scalar has no SQL type (<see cref="IrScalar.Sql"/> is null): <c>sql</c> is the database's type, which other
/// dialects and backends read, and a JSON Schema type is not one. Its <c>declared</c> null reason, and the override the IR
/// lists, say where it came from.</para>
/// A declaration is a claim, and is checked at runtime like the inferred types: the generated validator refuses output that
/// does not fit it. So a wrong declaration fails loudly, on real data.
/// </remarks>
internal sealed class DeclaredSchema(string file, JsonValue root, string rootPath, List<ConfigProblem> problems, string procedure, Dictionary<string, IrObjectType> types)
{
    private static readonly HashSet<string> Annotations = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$anchor", "$comment", "$defs", "definitions", "title", "description", "examples", "default", "deprecated", "readOnly", "writeOnly",
    };

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        "type", "format", "properties", "required", "items", "additionalProperties", "minItems", "maxItems", "$ref",
    };

    private readonly Stack<string> refs = new();

    private IrNullReason Declared => new(IrNullRules.Declared, procedure);

    /// <summary>A rowset: the schema of one row, an object whose properties are its columns, each always there.</summary>
    public IrResult? Rowset(int index)
    {
        var irPath = $"results[{index.ToString(CultureInfo.InvariantCulture)}][]";
        var pushed = refs.Count;
        try
        {
            return Row(root, rootPath, irPath);
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
            Error(value, at, "a rowset is declared by the schema of one row: an object with \"properties\", one for each column.");
            return null;
        }

        Keywords(schema, at);
        var properties = Members(schema)["properties"];
        if (properties.Value is not JsonObjectValue columns)
        {
            Error(properties.Value, Child(at, "properties"), "must be an object: each column's name and schema.");
            return null;
        }

        if (Members(schema).TryGetValue("required", out var required)
            && (required.Value is not JsonArrayValue list || !columns.Members.All(c => list.Items.Any(i => i is JsonStringValue s && s.Value == c.Name))))
        {
            Error(required.Value, Child(at, "required"), "every column of a rowset is always there, so \"required\" must list them all, or be left out.");
            return null;
        }

        var fields = new List<IrField>();
        foreach (var column in columns.Members)
        {
            var columnPath = Child(Child(at, "properties"), column.Name);
            if (Node(column.Value, columnPath, irPath + IrPath.Key(column.Name)) is not var (type, nullable))
                continue;
            if (type is not IrScalar)
            {
                Error(column.Value, columnPath, "a rowset's column holds a scalar: a string, number, integer or boolean. JSON a column holds is a string.");
                continue;
            }

            fields.Add(new IrField(column.Name, type, IrPresence.Always, nullable ? [Declared] : []));
        }

        return new IrResult(IrEncoding.Rowset, new IrArray(Object(fields, irPath)), []);
    }

    /// <summary>A JSON result: the schema of the document FOR JSON writes, which may be left out when it matches no rows.</summary>
    public IrResult? Json(int index, bool mayBeAbsent)
    {
        var irPath = $"results[{index.ToString(CultureInfo.InvariantCulture)}]";
        if (Node(root, rootPath, irPath) is not var (type, nullable))
            return null;
        if (nullable)
        {
            Error(root, rootPath, "the JSON document itself is never null; when a query may match no rows, use \"mayBeAbsent\": true.");
            return null;
        }

        return new IrResult(IrEncoding.Json, type, mayBeAbsent ? [Declared] : []);
    }

    /// <summary>A schema's type, and whether it may be null; null when it cannot be translated (the problem is reported).</summary>
    private (IrType Type, bool Nullable)? Node(JsonValue value, string at, string irPath)
    {
        if (value is JsonObjectValue withRef && Members(withRef).ContainsKey("$ref"))
        {
            // The target stays on the stack while its definition is read, so a definition that refers to itself, at any
            // depth, is found rather than followed for ever.
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
        }

        if (value is not JsonObjectValue schema)
        {
            Error(value, at, "must be a schema: an object with a \"type\".");
            return null;
        }

        var types = Types(schema, at);
        if (types is null)
            return null;
        var nullable = types.Contains("null");
        var nonNull = types.Where(t => t != "null").ToList();
        if (nonNull.Count == 0)
        {
            Error(schema, at, Members(schema).ContainsKey("type") ? "needs a type other than \"null\"." : "needs a \"type\".");
            return null;
        }

        if (nonNull.Count > 1)
        {
            Error(Members(schema)["type"].Value, Child(at, "type"), "can have only one type besides \"null\": a value of several types is not supported yet.");
            return null;
        }

        Keywords(schema, at);
        var members = Members(schema);
        var format = members.TryGetValue("format", out var f) && f.Value is JsonStringValue fs ? fs.Value : null;
        var type = nonNull[0] switch
        {
            "object" => ObjectNode(schema, at, irPath),
            "array" => ArrayNode(schema, at, irPath),
            "string" => format switch
            {
                null => new IrScalar(IrScalarKind.String, null, null),
                "date" => new IrScalar(IrScalarKind.Date, null, null),
                "time" => new IrScalar(IrScalarKind.Time, null, null),
                "date-time" => new IrScalar(IrScalarKind.Datetime, IrFormats.Offset, null),
                "date-time-local" => new IrScalar(IrScalarKind.Datetime, IrFormats.Local, null),
                "uuid" => new IrScalar(IrScalarKind.Uuid, null, null),
                _ => Unchecked(new IrScalar(IrScalarKind.String, null, null)),
            },
            "integer" => format switch
            {
                null => new IrScalar(IrScalarKind.Number, null, null),
                "int32" => new IrScalar(IrScalarKind.Number, IrFormats.Int32, null),
                "int64" => new IrScalar(IrScalarKind.Number, IrFormats.Int64, null),
                _ => Unchecked(new IrScalar(IrScalarKind.Number, null, null)),
            },
            "number" => format is null ? new IrScalar(IrScalarKind.Number, null, null) : Unchecked(new IrScalar(IrScalarKind.Number, null, null)),
            "boolean" => format is null ? new IrScalar(IrScalarKind.Boolean, null, null) : Unchecked(new IrScalar(IrScalarKind.Boolean, null, null)),
            var other => Invalid(other),
        };
        return type is null ? null : (type, nullable);

        IrScalar Unchecked(IrScalar scalar)
        {
            Warning(f!.Value, Child(at, "format"), $"\"{format}\" is not a format haruspex checks, so the generated schema does not check it.");
            return scalar;
        }

        IrType? Invalid(string other)
        {
            Error(members["type"].Value, Child(at, "type"), $"\"{other}\" is not a JSON Schema type: use string, integer, number, boolean, object, array or null.");
            return null;
        }
    }

    private IrType? ObjectNode(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);
        if (!members.TryGetValue("properties", out var properties))
        {
            // An object whose keys are data: every value of one type, like what json_objectagg builds.
            if (members.TryGetValue("additionalProperties", out var values) && values.Value is JsonObjectValue)
            {
                return Node(values.Value, Child(at, "additionalProperties"), irPath + "{}") is var (type, nullable)
                    ? new IrRecord(type, nullable ? [Declared] : [])
                    : null;
            }

            Error(schema, at, "needs \"properties\" (its keys), or \"additionalProperties\" (the type of every value, for an object whose keys are data).");
            return null;
        }

        if (properties.Value is not JsonObjectValue keys)
        {
            Error(properties.Value, Child(at, "properties"), "must be an object: each key's name and schema.");
            return null;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (members.TryGetValue("required", out var list))
        {
            if (list.Value is not JsonArrayValue array || array.Items.Any(i => i is not JsonStringValue))
            {
                Error(list.Value, Child(at, "required"), "must be an array of the keys that are always there.");
                return null;
            }

            foreach (var item in array.Items.Cast<JsonStringValue>())
            {
                if (!keys.Members.Any(k => k.Name == item.Value))
                    Error(item, Child(at, "required"), $"names \"{item.Value}\", which is not in \"properties\".");
                required.Add(item.Value);
            }
        }

        if (members.TryGetValue("additionalProperties", out var extra) && extra.Value is JsonObjectValue)
            Warning(extra.Value, Child(at, "additionalProperties"), "is not supported beside \"properties\" yet, so the generated schema does not check it; the \"objects\" setting decides what happens to keys it does not list.");

        var fields = new List<IrField>();
        foreach (var key in keys.Members)
        {
            var keyPath = Child(Child(at, "properties"), key.Name);
            if (Node(key.Value, keyPath, irPath + IrPath.Key(key.Name)) is not var (type, nullable))
                continue;

            // The IR keeps presence apart from nullability, as FOR JSON does: a key it leaves out is never null, and one it
            // keeps may be. A key that may be both left out and null is not one FOR JSON writes, and is not expressible yet.
            if (required.Contains(key.Name))
            {
                fields.Add(new IrField(key.Name, type, IrPresence.Always, nullable ? [Declared] : []));
            }
            else if (!nullable)
            {
                fields.Add(new IrField(key.Name, type, IrPresence.OmittedWhenNull, [Declared]));
            }
            else
            {
                Error(key.Value, keyPath, "may be both left out and null, which haruspex can't express yet: FOR JSON leaves a null out, so make the key required, or not nullable.");
            }
        }

        return Object(fields, irPath);
    }

    private IrArray? ArrayNode(JsonObjectValue schema, string at, string irPath)
    {
        var members = Members(schema);
        if (!members.TryGetValue("items", out var items))
        {
            Error(schema, at, "needs \"items\", the schema of its items.");
            return null;
        }

        if (Node(items.Value, Child(at, "items"), irPath + "[]") is not var (type, nullable))
            return null;
        return new IrArray(type)
        {
            ItemsNullableBecause = nullable ? [Declared] : [],
            MinItems = Count(members, "minItems", at),
            MaxItems = Count(members, "maxItems", at),
        };
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

        Error(member.Value, Child(at, keyword), "must be a whole number, zero or more.");
        return null;
    }

    private IrRef Object(List<IrField> fields, string irPath)
    {
        var key = $"{procedure}:{irPath}";
        var candidate = key;
        for (var n = 2; types.ContainsKey(candidate); n++)
            candidate = key + "~" + n.ToString(CultureInfo.InvariantCulture);
        types[candidate] = new IrObjectType(fields) { Identity = new IrTypeIdentity(IrTypeOwner.Procedure, procedure, irPath) };
        return new IrRef(candidate);
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
                Error(type.Value, Child(at, "type"), "must be a type's name, or an array of them.");
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
            Warning(member, Child(at, member.Name), "is not supported yet, so the generated schema does not check it.");
        }
    }

    /// <summary>
    /// The definition a <c>$ref</c> names in the same schema's <c>$defs</c> or <c>definitions</c>, its target pushed on the
    /// stack of references; null when it names none, or one already on the stack (reported).
    /// </summary>
    private (JsonValue Value, string Path)? Follow(JsonObjectValue schema, string path)
    {
        var reference = Members(schema)["$ref"];
        var refPath = Child(path, "$ref");
        if (reference.Value is not JsonStringValue { Value: var target }
            || (Split(target, "#/$defs/") ?? Split(target, "#/definitions/")) is not var (container, name))
        {
            Error(reference.Value, refPath, "can refer only to \"#/$defs/…\" or \"#/definitions/…\" of the same schema.");
            return null;
        }

        foreach (var sibling in schema.Members.Where(m => m.Name != "$ref" && !Annotations.Contains(m.Name)))
            Warning(sibling, Child(path, sibling.Name), "is ignored beside \"$ref\".");

        if (root is not JsonObjectValue top || !Members(top).TryGetValue(container, out var defs) || defs.Value is not JsonObjectValue definitions
            || definitions.Members.FirstOrDefault(d => d.Name == name) is not { } definition)
        {
            Error(reference.Value, refPath, $"refers to \"{target}\", which the schema does not define.");
            return null;
        }

        if (refs.Contains(target))
        {
            Error(reference.Value, refPath, $"refers to \"{target}\" inside itself: a recursive schema is not supported yet.");
            return null;
        }

        refs.Push(target);
        return (definition.Value, Child(Child(rootPath, container), name));
    }

    private static (string Container, string Name)? Split(string target, string prefix) =>
        target.StartsWith(prefix, StringComparison.Ordinal) && target.Length > prefix.Length && !target[prefix.Length..].Contains('/', StringComparison.Ordinal)
            ? (prefix[2..^1], target[prefix.Length..])
            : null;

    private static Dictionary<string, JsonMember> Members(JsonObjectValue schema)
    {
        var members = new Dictionary<string, JsonMember>(StringComparer.Ordinal);
        foreach (var member in schema.Members)
            members[member.Name] = member;
        return members;
    }

    private void Error(JsonValue value, string path, string message) =>
        problems.Add(new ConfigProblem(ConfigSeverity.Error, file, new ConfigLocation(path, value.Line, value.Column), message));

    private void Warning(JsonValue value, string path, string message) =>
        problems.Add(new ConfigProblem(ConfigSeverity.Warning, file, new ConfigLocation(path, value.Line, value.Column), message));

    private void Warning(JsonMember member, string path, string message) =>
        problems.Add(new ConfigProblem(ConfigSeverity.Warning, file, new ConfigLocation(path, member.Line, member.Column), message));

    private static string Child(string path, string key) => ConfigPath.Child(path, key);
}
