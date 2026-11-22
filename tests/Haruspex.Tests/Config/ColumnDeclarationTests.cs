using System.Security.Cryptography;
using System.Text;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Tests.Config;

/// <summary>
/// A column declared to hold JSON of a schema (<c>columns["shop.orders.settings"].json</c>), read from the config file into the
/// IR: the schema file and the files it reaches, a pointer inside one, and each keyword read, read wider with a warning, or
/// refused. What the inference does with it is in the Inference tests' <c>DeclaredColumnTests</c>. Why:
/// docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md.
/// </summary>
public partial class ColumnDeclarationTests
{
    private const string Key = "shop.orders.settings";

    /// <summary>The config file declaring <see cref="Key"/> to hold JSON of <paramref name="json"/>, read in <paramref name="dir"/>.</summary>
    private static Outcome<HaruspexConfig> Read(TempDirectory dir, string json = "schemas/settings.schema.json", string key = Key) =>
        ConfigReader.Read(Encoding.UTF8.GetBytes($$"""{ "columns": { "{{key}}": { "json": "{{json}}" } } }"""), Path.Combine(dir.Path, "haruspex.json"), dir.Path);

    /// <summary>The one declaration of a config that reads with no problem.</summary>
    private static ColumnDeclaration Declared(TempDirectory dir, string schema)
    {
        dir.File("schemas/settings.schema.json", schema);
        var read = Read(dir);
        Assert.Empty(read.Diagnostics.Select(d => d.ToString()));
        return Assert.Single(read.Expected().Columns);
    }

    private static string[] Problems(Outcome<HaruspexConfig> read) => [.. read.Diagnostics.Select(d => d.ToString())];

    /// <summary>The problems a schema gives, with the folder left out of their file names.</summary>
    private static string[] Problems(TempDirectory dir, string schema)
    {
        dir.File("schemas/settings.schema.json", schema);
        return Problems(dir, Read(dir));
    }

    /// <summary>
    /// The problems of <paramref name="read"/>, with the folder left out of their file names. The config layer holds each file by
    /// its full path and a diagnostic prints it with <c>/</c> on every platform (<see cref="DiagnosticFormatter.PathAsShown"/>), so
    /// the folder is left out in that form: on Windows, <c>C:/Users/…/</c>, not <c>C:\Users\…\</c>.
    /// </summary>
    private static string[] Problems(TempDirectory dir, Outcome<HaruspexConfig> read) =>
        [.. Problems(read).Select(p => p.Replace(DiagnosticFormatter.PathAsShown(dir.Path) + "/", "", StringComparison.Ordinal))];

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>The fields of the object type a reference names in the declaration.</summary>
    private static Haruspex.Collections.EquatableArray<IrField> Fields(ColumnDeclaration declaration, IrType type) => declaration.Types[Assert.IsType<IrRef>(type).Name].Fields;

    private static IrField Field(ColumnDeclaration declaration, string name) => Fields(declaration, declaration.Content).Single(f => f.Name == name);

    // ---- the declaration -----------------------------------------------------------------------------------------

    [Fact]
    public void A_declaration_names_its_column_and_records_its_schema_file_and_the_file_s_hash()
    {
        using var dir = new TempDirectory();
        const string Schema = """{ "type": "object", "properties": { "mode": { "type": "string" } }, "required": ["mode"] }""";

        var declared = Declared(dir, Schema);

        Assert.Equal(new ObjectName("shop", "orders"), declared.Table);
        Assert.Equal("settings", declared.Column);
        Assert.Equal(Key, declared.Record.Column);
        Assert.Equal("schemas/settings.schema.json", declared.Record.Schema);
        Assert.Null(declared.Record.Pointer);
        Assert.Equal(new Dictionary<string, string> { ["schemas/settings.schema.json"] = Sha256(Schema) }, declared.Record.Files);
        Assert.Equal(new IrField("mode", new IrScalar(IrScalarKind.String, null, null), IrPresence.Always, []), Field(declared, "mode"));
    }

    [Theory]
    [InlineData("[sales.eu].orders.settings", "sales.eu", "orders", "settings")]
    [InlineData("shop.[order]].lines].settings", "shop", "order].lines", "settings")]
    public void A_part_of_the_key_that_holds_a_dot_or_a_bracket_is_written_in_brackets(string key, string schema, string table, string column)
    {
        Assert.True(ColumnDeclaration.TryParseKey(key, out var parsedTable, out var parsedColumn, out _));
        Assert.Equal((new ObjectName(schema, table), column), (parsedTable, parsedColumn));
    }

    [Theory]
    [InlineData("shop.orders")]
    [InlineData("a.b.c.d")]
    [InlineData("shop..settings")]
    [InlineData("[shop.orders.settings")]
    public void A_key_that_is_not_three_parts_is_refused(string key)
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", "{}");

        Assert.Contains("error HAR3006", Assert.Single(Problems(Read(dir, key: key))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pointer_after_the_file_names_the_schema_inside_it()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/party.schema.json", """
            { "$defs": {
                "person": { "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] },
                "company": { "type": "object", "properties": { "vat": { "type": "string" } }, "required": ["vat"] } } }
            """);

        var declared = Assert.Single(Read(dir, "schemas/party.schema.json#/$defs/company").Expected().Columns);

        Assert.Equal("/$defs/company", declared.Record.Pointer);
        Assert.Equal(["vat"], Fields(declared, declared.Content).Select(f => f.Name));
    }

    [Fact]
    public void A_pointer_that_names_nothing_is_refused()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/party.schema.json", """{ "$defs": {} }""");

        Assert.Equal(
            ["haruspex.json:1:50: error HAR3113: columns[\"shop.orders.settings\"].json: points at \"/$defs/person\" in schemas/party.schema.json, which names nothing: a pointer is a JSON Pointer, such as #/$defs/person."],
            Problems(dir, Read(dir, "schemas/party.schema.json#/$defs/person")));
    }

    [Fact]
    public void A_reference_to_another_file_is_read_relative_to_the_schema_and_its_hash_recorded_too()
    {
        using var dir = new TempDirectory();
        const string Common = """{ "$defs": { "money": { "type": "object", "properties": { "amount": { "type": "number" } }, "required": ["amount"] } } }""";
        dir.File("schemas/common/money.schema.json", Common);

        var declared = Declared(dir, """{ "type": "object", "properties": { "limit": { "$ref": "common/money.schema.json#/$defs/money" } }, "required": ["limit"] }""");

        Assert.Equal(["schemas/common/money.schema.json", "schemas/settings.schema.json"], declared.Record.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Sha256(Common), declared.Record.Files["schemas/common/money.schema.json"]);
        Assert.Equal(["amount"], Fields(declared, Field(declared, "limit").Type).Select(f => f.Name));
    }

    [Fact]
    public void A_reference_inside_another_file_refers_into_that_file()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/common.schema.json", """{ "$defs": { "code": { "$ref": "#/$defs/text" }, "text": { "type": "string" } } }""");

        var declared = Declared(dir, """{ "type": "object", "properties": { "code": { "$ref": "common.schema.json#/$defs/code" } }, "required": ["code"], "$defs": { "text": { "type": "integer" } } }""");

        Assert.Equal(IrScalarKind.String, Assert.IsType<IrScalar>(Field(declared, "code").Type).Kind);
    }

    [Fact]
    public void A_problem_in_a_file_a_reference_reaches_is_reported_in_that_file()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/common.schema.json", "{\n  \"$defs\": {\n    \"bad\": { \"type\": \"date\" }\n  }\n}\n");

        Assert.Equal(
            ["schemas/common.schema.json:3:22: error HAR3107: $defs.bad.type: \"date\" is not a JSON Schema type: use string, integer, number, boolean, object, array or null."],
            Problems(dir, """{ "$ref": "common.schema.json#/$defs/bad" }"""));
    }

    [Fact]
    public void A_reference_to_a_schema_on_the_network_is_refused()
    {
        using var dir = new TempDirectory();

        Assert.Equal(
            ["schemas/settings.schema.json:1:11: error HAR3112: $ref: refers to https://example.com/settings.json, which is not a local file: haruspex never fetches a schema, so keep a copy beside this one and refer to it."],
            Problems(dir, """{ "$ref": "https://example.com/settings.json" }"""));
    }

    [Fact]
    public void A_reference_back_through_another_file_is_a_recursive_schema()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/node.schema.json", """{ "type": "object", "properties": { "next": { "$ref": "settings.schema.json" } } }""");

        Assert.Contains("error HAR3114", Assert.Single(Problems(dir, """{ "$ref": "node.schema.json" }""")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_block_needs_json_and_nothing_else()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", "{}");

        Assert.Contains("needs \"json\"", Assert.Single(Problems(ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "columns": { "shop.orders.settings": {} } }"""), "haruspex.json", dir.Path))), StringComparison.Ordinal);
        Assert.Contains("error HAR3004", Assert.Single(Problems(ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "columns": { "shop.orders.settings": { "json": "schemas/settings.schema.json", "why": "x" } } }"""), "haruspex.json", dir.Path))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_declared_twice_under_two_spellings_is_refused()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", "{}");

        var read = ConfigReader.Read(Encoding.UTF8.GetBytes("""{ "columns": { "shop.orders.settings": { "json": "schemas/settings.schema.json" }, "shop.orders.SETTINGS": { "json": "schemas/settings.schema.json" } } }"""), "haruspex.json", dir.Path);

        Assert.Contains("error HAR3003", Assert.Single(Problems(read)), StringComparison.Ordinal);
    }

    // ---- presence and null -------------------------------------------------------------------------------------

    [Fact]
    public void A_key_not_required_may_be_left_out_whatever_its_value_and_is_null_only_when_its_type_allows_null()
    {
        using var dir = new TempDirectory();

        var declared = Declared(dir, """
            { "type": "object", "required": ["id", "note"], "properties": {
                "id": { "type": "integer" }, "note": { "type": ["string", "null"] },
                "label": { "type": "string" }, "hint": { "type": ["string", "null"] } } }
            """);

        IrNullReason[] declaredNull = [new(IrNullRule.Declared, Key)];
        Assert.Equal((IrPresence.Always, false), (Field(declared, "id").Presence, Field(declared, "id").Nullable));
        Assert.Equal(IrPresence.Always, Field(declared, "note").Presence);
        Assert.Equal(declaredNull, Field(declared, "note").NullableBecause);
        Assert.Equal((IrPresence.Optional, false), (Field(declared, "label").Presence, Field(declared, "label").Nullable));
        Assert.Equal(IrPresence.Optional, Field(declared, "hint").Presence);
        Assert.Equal(declaredNull, Field(declared, "hint").NullableBecause);
    }

    [Theory]
    [InlineData("""{ "type": ["object", "null"], "properties": {} }""")]
    [InlineData("""{ "anyOf": [{ "type": "object", "properties": {} }, { "type": "null" }] }""")]
    public void Json_null_at_the_top_of_a_column_s_schema_is_refused(string schema)
    {
        using var dir = new TempDirectory();

        Assert.Contains("error HAR3115", Assert.Single(Problems(dir, schema)), StringComparison.Ordinal);
    }

    // ---- any JSON ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("""{ "description": "whatever the step needs" }""")]
    public void A_schema_that_allows_every_value_is_any_json(string schema)
    {
        using var dir = new TempDirectory();

        Assert.Equal(new IrAnyJson(), Declared(dir, schema).Content);
    }

    [Fact]
    public void An_object_or_array_whose_contents_are_not_described_holds_any_json()
    {
        using var dir = new TempDirectory();

        var declared = Declared(dir, """{ "type": "object", "properties": { "extra": {}, "bag": { "type": "object" }, "list": { "type": "array" } }, "required": ["extra", "bag", "list"] }""");

        Assert.Equal(new IrAnyJson(), Field(declared, "extra").Type);
        Assert.Equal(new IrRecord(new IrAnyJson(), []), Field(declared, "bag").Type);
        Assert.Equal(new IrArray(new IrAnyJson()), Field(declared, "list").Type);
    }

    [Fact]
    public void A_keyword_in_a_schema_without_a_type_is_not_checked_and_says_so()
    {
        using var dir = new TempDirectory();

        Assert.Equal(
            ["schemas/settings.schema.json:1:3: warning HAR3151: properties: applies only to the type it is written for, and this schema gives no \"type\": the value may be any JSON, so the generated schema does not check it."],
            Problems(dir, """{ "properties": { "a": { "type": "string" } } }"""));
    }

    // ---- value sets, unions and lengths ------------------------------------------------------------------------

    [Fact]
    public void Enum_and_const_give_a_value_set_the_declaration_states()
    {
        using var dir = new TempDirectory();

        var declared = Declared(dir, """
            { "type": "object", "required": ["mode", "level", "kind"], "properties": {
                "mode": { "type": "string", "enum": ["fast", "slow"] },
                "level": { "enum": [1, 2.5, null] },
                "kind": { "const": "basic" } } }
            """);

        IrFactSource from = new(IrFactSourceKind.Declared, Key);
        Assert.Equal(
            new IrScalar(IrScalarKind.String, null, null, new IrFacets(Values: new IrValueSet([new("fast", IsString: true), new("slow", IsString: true)], IrValueMatch.Exact, [from]))),
            Field(declared, "mode").Type);
        Assert.Equal(
            new IrScalar(IrScalarKind.Number, null, null, new IrFacets(Values: new IrValueSet([new("1", IsString: false), new("2.5", IsString: false)], IrValueMatch.Exact, [from]))),
            Field(declared, "level").Type);
        Assert.True(Field(declared, "level").Nullable);
        Assert.Equal(new IrValueSet([new("basic", IsString: true)], IrValueMatch.Exact, [from]), Assert.IsType<IrScalar>(Field(declared, "kind").Type).Facets?.Values);
    }

    [Fact]
    public void An_enum_of_strings_and_numbers_is_a_union_of_the_two_sets()
    {
        using var dir = new TempDirectory();

        var union = Assert.IsType<IrUnion>(Declared(dir, """{ "enum": ["auto", 0, 1] }""").Content);

        Assert.Equal([IrScalarKind.String, IrScalarKind.Number], union.Variants.Select(v => Assert.IsType<IrScalar>(v).Kind));
    }

    [Fact]
    public void An_enum_of_another_kind_of_value_is_not_checked_and_its_type_is_kept()
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", """{ "type": "boolean", "enum": [true] }""");

        var read = Read(dir);

        Assert.Contains("warning HAR3151", Assert.Single(Problems(read)), StringComparison.Ordinal);
        Assert.Equal(new IrScalar(IrScalarKind.Boolean, null, null), Assert.Single(read.Expected().Columns).Content);
    }

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void Any_of_and_one_of_are_a_union_of_their_branches_nullable_when_one_is_null(string keyword)
    {
        using var dir = new TempDirectory();

        var declared = Declared(dir, $$"""
            { "type": "object", "required": ["step"], "properties": { "step": { "{{keyword}}": [
                { "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] },
                { "type": "object", "properties": { "delay": { "type": "integer" } }, "required": ["delay"] },
                { "type": "null" } ] } } }
            """);

        var step = Field(declared, "step");
        Assert.Equal([["url"], ["delay"]], Assert.IsType<IrUnion>(step.Type).Variants.Select(v => Fields(declared, v).Select(f => f.Name).ToArray()));
        Assert.True(step.Nullable);
    }

    [Fact]
    public void A_union_with_any_json_in_it_is_any_json()
    {
        using var dir = new TempDirectory();

        Assert.Equal(new IrAnyJson(), Declared(dir, """{ "anyOf": [{ "type": "string" }, {}] }""").Content);
    }

    [Fact]
    public void Several_types_are_a_union_each_with_the_keywords_written_for_it()
    {
        using var dir = new TempDirectory();

        var union = Assert.IsType<IrUnion>(Declared(dir, """{ "type": ["string", "array"], "items": { "type": "integer" } }""").Content);

        Assert.Equal(new IrScalar(IrScalarKind.String, null, null), union.Variants[0]);
        Assert.Equal(new IrArray(new IrScalar(IrScalarKind.Number, null, null)), union.Variants[1]);
    }

    [Fact]
    public void Min_length_and_max_length_are_the_lengths_the_declaration_states_the_maximum_in_code_points()
    {
        using var dir = new TempDirectory();

        var facets = Assert.IsType<IrScalar>(Declared(dir, """{ "type": "string", "minLength": 2, "maxLength": 128 }""").Content).Facets;

        IrFactSource from = new(IrFactSourceKind.Declared, Key);
        Assert.Equal(new IrMinLength(2, from), facets?.MinLength);
        Assert.Equal(new IrLength(128, IsFixed: false, IrLengthUnit.CodePoints, IrByteEncoding.Unknown, from), facets?.Length);
    }

    [Theory]
    [InlineData("""{ "type": "integer" }""", null)]
    [InlineData("""{ "type": "integer", "format": "int64" }""", IrFormat.Int64)]
    [InlineData("""{ "type": "integer", "format": "int32" }""", IrFormat.Int32)]
    public void An_integer_is_a_number_and_one_of_format_int64_follows_the_rule_for_large_integers(string schema, IrFormat? format)
    {
        using var dir = new TempDirectory();

        Assert.Equal(new IrScalar(IrScalarKind.Number, format, null), Declared(dir, schema).Content);
    }

    [Fact]
    public void Additional_properties_false_says_nothing_more_and_a_schema_beside_properties_is_not_checked()
    {
        using var dir = new TempDirectory();

        Assert.Empty(Problems(dir, """{ "type": "object", "properties": { "a": { "type": "string" } }, "additionalProperties": false }"""));
        Assert.Contains(
            "warning HAR3151: additionalProperties: is not supported beside \"properties\" yet",
            Assert.Single(Problems(dir, """{ "type": "object", "properties": { "a": { "type": "string" } }, "additionalProperties": { "type": "string" } }""")),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "type": "object", "properties": { "id": { "type": "string" } }, "required": ["id"], "patternProperties": { "^x-": { "type": "string" } }, "additionalProperties": false }""")]
    [InlineData("""{ "type": "object", "patternProperties": { "^x-": { "type": "string" } }, "additionalProperties": false }""")]
    public void Pattern_properties_keep_an_object_open_whatever_additional_properties_says(string schema)
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", schema);

        var declared = Assert.Single(Read(dir).Expected().Columns);

        // Keys of the pattern may be there, of the pattern's schema, which isn't read: so any key, of any JSON.
        Assert.True(declared.Content switch
        {
            IrRef reference => declared.Types[reference.Name].Open,
            IrRecord { Values: IrAnyJson } => true,
            _ => false,
        });
    }

    [Theory]
    [InlineData("""{ "type": "array", "prefixItems": [{ "type": "string" }], "items": { "type": "number" } }""")]
    [InlineData("""{ "type": "array", "prefixItems": [{ "type": "string" }, { "type": "integer" }], "items": false }""")]
    public void Items_beside_prefix_items_speak_only_for_the_items_after_it_so_every_item_is_any_json(string schema)
    {
        using var dir = new TempDirectory();
        dir.File("schemas/settings.schema.json", schema);

        var read = Read(dir);

        Assert.Contains("prefixItems", Assert.Single(Problems(read)), StringComparison.Ordinal);
        Assert.Equal(new IrArray(new IrAnyJson()), Assert.Single(read.Expected().Columns).Content);
    }

    [Fact]
    public void Items_false_alone_allows_no_item()
    {
        using var dir = new TempDirectory();

        Assert.Equal(new IrArray(new IrAnyJson()) { MaxItems = 0 }, Declared(dir, """{ "type": "array", "items": false }""").Content);
    }

    [Fact]
    public void A_schema_s_hash_is_of_its_text_whatever_its_line_endings_and_byte_order_mark()
    {
        const string Lf = "{\n  \"type\": \"object\",\n  \"properties\": {}\n}\n";
        string[] copies = [Lf, Lf.Replace("\n", "\r\n", StringComparison.Ordinal), Lf.Replace("\n", "\r", StringComparison.Ordinal), Encoding.UTF8.GetString(Encoding.UTF8.Preamble) + Lf];

        var hashes = copies.Select(text =>
        {
            using var dir = new TempDirectory();
            dir.File("schemas/settings.schema.json", text);
            return Assert.Single(Read(dir).Expected().Columns).Record.Files["schemas/settings.schema.json"];
        });

        Assert.Single(hashes.Distinct());
    }

    [Fact]
    public void A_column_s_json_on_the_network_is_refused_as_a_reference_to_one_is()
    {
        using var dir = new TempDirectory();

        Assert.Equal(
            ["haruspex.json:1:50: error HAR3112: columns[\"shop.orders.settings\"].json: refers to https://example.com/settings.json, which is not a local file: haruspex never fetches a schema, so keep a copy beside this one and refer to it."],
            Problems(dir, Read(dir, "https://example.com/settings.json")));
    }

    [Fact]
    public void A_pattern_is_not_checked_and_says_so()
    {
        using var dir = new TempDirectory();

        Assert.Equal(
            ["schemas/settings.schema.json:1:21: warning HAR3151: pattern: is not supported yet, so the generated schema does not check it."],
            Problems(dir, """{ "type": "string", "pattern": "^[a-z]+$" }"""));
    }
}
