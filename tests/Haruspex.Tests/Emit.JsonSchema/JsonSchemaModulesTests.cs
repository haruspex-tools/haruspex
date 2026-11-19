using System.Text.Json.Nodes;
using Haruspex.Collections;
using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>
/// The JSON Schema document split into a document per module, with the named types two or more modules reach in a shared
/// document that the modules refer to by a relative <c>$ref</c>
/// (docs/decisions/0075-what-several-modules-share-is-written-once-in-a-shared-file.md).
/// </summary>
public class JsonSchemaModulesTests
{
    private static readonly IrScalar Number = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrObjectType Type(params IrType[] fields) =>
        new([.. fields.Select((t, i) => new IrField("f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), t, IrPresence.Always, []))]);

    private static IrObject Procedure(string id, string name, IrType result) =>
        new(id, name, IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, result, [])]);

    // GetOrder and GetProduct, in two modules, both reach Total; only GetOrder reaches Line.
    private static readonly IrDocument Shop = new(
        new IrSource("tsql"),
        [Procedure("sales.get_order", "GetOrder", new IrRef("Order")), Procedure("shop.get_product", "GetProduct", new IrRef("Product"))],
        new Dictionary<string, IrObjectType>
        {
            ["Order"] = Type(new IrRef("Total"), new IrArray(new IrRef("Line"))),
            ["Line"] = Type(Number),
            ["Product"] = Type(new IrRef("Total")),
            ["Total"] = Type(Number),
        },
        []);

    private static EquatableMap<string, string> Split(IrDocument document) =>
        JsonSchemaEmitter.Emit(document, new JsonSchemaOptions(), IrModules.Of(document, document.Objects.ToDictionary(o => o.Id, o => o.Id[..o.Id.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal))).Expected();

    private static JsonObject Definitions(string document) => JsonNode.Parse(document)!["$defs"]!.AsObject();

    [Fact]
    public void Each_module_defines_its_procedures_and_the_types_it_alone_reaches()
    {
        var modules = Split(Shop);

        Assert.Equal(["sales", "shared", "shop"], modules.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["GetOrder", "Line", "Order"], Definitions(modules["sales"]).Select(d => d.Key));
        Assert.Equal(["GetProduct", "Product"], Definitions(modules["shop"]).Select(d => d.Key));
        Assert.Equal(["Total"], Definitions(modules[IrModules.Shared]).Select(d => d.Key));
    }

    [Fact]
    public void A_type_in_the_shared_document_is_referred_to_relative_to_it_and_a_module_s_own_within_it()
    {
        var order = Definitions(Split(Shop)["sales"])["Order"]!["properties"]!;

        Assert.Equal("shared.jsonschema.json#/$defs/Total", (string?)order["f0"]!["$ref"]);
        Assert.Equal("#/$defs/Line", (string?)order["f1"]!["items"]!["$ref"]);
    }

    [Fact]
    public void Every_definition_of_the_unsplit_document_is_in_one_document_as_it_was_but_for_where_a_reference_points()
    {
        var whole = Definitions(JsonSchemaEmitter.Emit(Shop).Expected());

        var split = Split(Shop).Values
            .SelectMany(d => Definitions(d.Replace("shared.jsonschema.json#", "#", StringComparison.Ordinal)))
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(whole.Select(d => d.Key).Order(StringComparer.Ordinal), split.Select(d => d.Key));
        Assert.All(split, d => Assert.True(JsonNode.DeepEquals(whole[d.Key], d.Value), d.Key));
    }

    [Fact]
    public void The_shared_document_says_what_it_is_and_a_module_says_where_its_shared_types_are()
    {
        var modules = Split(Shop);

        Assert.Contains("two or more modules of this config share", (string?)JsonNode.Parse(modules[IrModules.Shared])!["description"], StringComparison.Ordinal);
        Assert.Contains("in shared.jsonschema.json", (string?)JsonNode.Parse(modules["sales"])!["description"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Added to Ajv under their file names, as a consumer adds them, every definition of every document compiles, so each
    /// relative <c>$ref</c> resolves; and a module's definition checks a value through the shared one (<c>src/split-schema.mjs</c>).
    /// </summary>
    [Fact]
    public void Every_reference_resolves_in_ajv_and_a_module_checks_values_through_the_shared_document()
    {
        EmitCheckPackage.RequireNode(RepositoryPaths.EmitCheck, "the split JSON Schema documents were not compiled together");
        using var folder = new TempDirectory();
        foreach (var (module, text) in Split(Shop))
            folder.File($"{module}.jsonschema.json", text);
        folder.File("checks.json", """
            [
              { "schema": "sales.jsonschema.json#/$defs/Order", "value": { "f0": { "f0": 1 }, "f1": [{ "f0": 2 }] }, "valid": true },
              { "schema": "sales.jsonschema.json#/$defs/Order", "value": { "f0": { "f0": "one" }, "f1": [] }, "valid": false },
              { "schema": "shop.jsonschema.json#/$defs/GetProduct", "value": { "f0": { "f0": 1.5 } }, "valid": false }
            ]
            """);

        EmitCheckPackage.EnsureInstalled(RepositoryPaths.EmitCheck);
        Assert.True(
            EmitCheckPackage.TryRun("node", [Path.Combine("src", "split-schema.mjs"), folder.Path], RepositoryPaths.EmitCheck, out var output, out var why),
            $"The split documents don't check together ({why}):\n{output}");
    }
}
