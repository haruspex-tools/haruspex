using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Haruspex.Tests;

/// <summary>
/// A validator for the part of JSON Schema 2020-12 that <c>schemas/haruspex.schema.json</c> uses, so the published schema
/// can be checked against examples without a dependency (CONTRIBUTING.md: none without a written reason). It refuses a
/// keyword it doesn't implement, so the schema can't start relying on one that nothing here checks.
/// </summary>
internal sealed class SchemaSubset(JsonElement root)
{
    private static readonly HashSet<string> Annotations = new(StringComparer.Ordinal) { "$schema", "$id", "title", "description", "$defs" };

    private static readonly HashSet<string> Implemented = new(StringComparer.Ordinal)
    {
        "type", "enum", "const", "properties", "additionalProperties", "required", "items", "minItems", "minLength", "pattern",
        "$ref", "dependentRequired", "dependentSchemas", "anyOf",
    };

    /// <summary>The ways <paramref name="instance"/> fails the schema, as JSON pointers with a reason; empty when it is valid.</summary>
    public List<string> Validate(JsonElement instance)
    {
        var errors = new List<string>();
        Check(root, instance, "", errors);
        return errors;
    }

    private void Check(JsonElement schema, JsonElement value, string at, List<string> errors)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            if (Annotations.Contains(keyword.Name))
                continue;
            if (!Implemented.Contains(keyword.Name))
                throw new NotSupportedException($"The schema uses \"{keyword.Name}\", which SchemaSubset does not implement.");

            var k = keyword.Value;
            switch (keyword.Name)
            {
                case "$ref":
                    var target = k.GetString()!;
                    if (!target.StartsWith("#/$defs/", StringComparison.Ordinal))
                        throw new NotSupportedException($"Only #/$defs/ references are implemented, not {target}.");
                    Check(root.GetProperty("$defs").GetProperty(target["#/$defs/".Length..]), value, at, errors);
                    break;
                case "type":
                    if (!HasType(value, k.GetString()!))
                        errors.Add($"{at}: is not of type {k.GetString()}");
                    break;
                case "enum":
                    if (!k.EnumerateArray().Any(e => JsonElement.DeepEquals(e, value)))
                        errors.Add($"{at}: is not one of the enum's values");
                    break;
                case "const":
                    if (!JsonElement.DeepEquals(k, value))
                        errors.Add($"{at}: is not {k.GetRawText()}");
                    break;
                case "properties" when value.ValueKind == JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject())
                    {
                        if (k.TryGetProperty(property.Name, out var sub))
                            Check(sub, property.Value, $"{at}/{property.Name}", errors);
                    }

                    break;
                case "additionalProperties" when value.ValueKind == JsonValueKind.Object:
                    var known = schema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal) : [];
                    foreach (var property in value.EnumerateObject().Where(p => !known.Contains(p.Name)))
                    {
                        if (k.ValueKind == JsonValueKind.False)
                            errors.Add($"{at}/{property.Name}: is not allowed");
                        else
                            Check(k, property.Value, $"{at}/{property.Name}", errors);
                    }

                    break;
                case "required" when value.ValueKind == JsonValueKind.Object:
                    foreach (var name in k.EnumerateArray().Select(n => n.GetString()!).Where(n => !value.TryGetProperty(n, out _)))
                        errors.Add($"{at}: needs {name}");
                    break;
                case "items" when value.ValueKind == JsonValueKind.Array:
                    var i = 0;
                    foreach (var item in value.EnumerateArray())
                        Check(k, item, $"{at}/{(i++).ToString(CultureInfo.InvariantCulture)}", errors);
                    break;
                case "minItems" when value.ValueKind == JsonValueKind.Array:
                    if (value.GetArrayLength() < k.GetInt32())
                        errors.Add($"{at}: has fewer than {k.GetInt32()} items");
                    break;
                case "minLength" when value.ValueKind == JsonValueKind.String:
                    // JSON Schema counts code points; every limit here is about emptiness, where the two agree.
                    if (value.GetString()!.EnumerateRunes().Count() < k.GetInt32())
                        errors.Add($"{at}: is shorter than {k.GetInt32()}");
                    break;
                case "pattern" when value.ValueKind == JsonValueKind.String:
                    if (!Regex.IsMatch(value.GetString()!, k.GetString()!, RegexOptions.None, TimeSpan.FromSeconds(1)))
                        errors.Add($"{at}: does not match {k.GetString()}");
                    break;
                case "dependentRequired" when value.ValueKind == JsonValueKind.Object:
                    foreach (var dependency in k.EnumerateObject().Where(d => value.TryGetProperty(d.Name, out _)))
                    {
                        foreach (var name in dependency.Value.EnumerateArray().Select(n => n.GetString()!).Where(n => !value.TryGetProperty(n, out _)))
                            errors.Add($"{at}: {dependency.Name} needs {name}");
                    }

                    break;
                case "dependentSchemas" when value.ValueKind == JsonValueKind.Object:
                    foreach (var dependency in k.EnumerateObject().Where(d => value.TryGetProperty(d.Name, out _)))
                        Check(dependency.Value, value, at, errors);
                    break;
                case "anyOf":
                    if (!k.EnumerateArray().Any(option => { var none = new List<string>(); Check(option, value, at, none); return none.Count == 0; }))
                        errors.Add($"{at}: matches none of anyOf");
                    break;
            }
        }
    }

    private static bool HasType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => throw new NotSupportedException($"Unknown type {type}."),
    };
}
