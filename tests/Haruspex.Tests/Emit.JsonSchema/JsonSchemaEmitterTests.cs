using System.Text.Json.Nodes;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Emit.JsonSchema;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.JsonSchema;

/// <summary>The JSON Schema emitter on hand-built IR, independent of any front end: what each IR fact becomes.</summary>
public class JsonSchemaEmitterTests
{
    private static readonly IrNullReason Column = new(IrNullRule.NullableColumn, "shop.basket.note");

    private static IrDocument OneResult(IrEncoding encoding, params IrField[] fields) => new(
        new IrSource("tsql"),
        [new IrObject("shop.list_baskets", "ListBaskets", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(encoding, new IrArray(new IrRef("ListBasketsRow")), [])])],
        new Dictionary<string, IrObjectType> { ["ListBasketsRow"] = new([.. fields]) },
        []);

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static JsonNode Emit(IrDocument document, JsonSchemaOptions? options = null) =>
        JsonNode.Parse(JsonSchemaEmitter.Emit(document, options ?? new JsonSchemaOptions()).Expected())!;

    private static JsonNode Definition(JsonNode document, string name) => document["$defs"]![name]!;

    /// <summary>The schema of the one field <c>v</c> of a rowset's row.</summary>
    private static JsonNode Value(IrType type, IrEncoding encoding = IrEncoding.Rowset) =>
        Definition(Emit(OneResult(encoding, Field("v", type))), "ListBasketsRow")["properties"]!["v"]!;

    /// <summary>A schema without its descriptions, which say why rather than what is accepted.</summary>
    private static JsonNode Bare(JsonNode node)
    {
        var copy = node.DeepClone();
        Strip(copy);
        return copy;

        static void Strip(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    obj.Remove("description");
                    foreach (var (_, child) in obj)
                        Strip(child);
                    break;
                case JsonArray array:
                    foreach (var child in array)
                        Strip(child);
                    break;
            }
        }
    }

    private static void AssertSchema(string expected, JsonNode actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), Bare(actual)), $"Expected {expected}, got {Bare(actual).ToJsonString()}");

    private static string Description(JsonNode node) => (string?)node["description"] ?? "";

    [Fact]
    public void Writes_a_2020_12_document_with_a_definition_for_each_procedure_and_each_type()
    {
        var document = Emit(OneResult(IrEncoding.Rowset, Field("id", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"))));

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)document["$schema"]);
        Assert.Equal(["ListBaskets", "ListBasketsRow"], document["$defs"]!.AsObject().Select(d => d.Key));
        AssertSchema("""{ "title": "shop.list_baskets", "type": "array", "items": { "$ref": "#/$defs/ListBasketsRow" } }""", Definition(document, "ListBaskets"));
        AssertSchema(
            """{ "type": "object", "properties": { "id": { "type": "integer", "minimum": -2147483648, "maximum": 2147483647 } }, "required": ["id"] }""",
            Definition(document, "ListBasketsRow"));
    }

    [Fact]
    public void Separates_absent_keys_from_null_values()
    {
        var text = new IrScalar(IrScalarKind.String, null, null);
        var row = Definition(Emit(OneResult(
            IrEncoding.Json,
            new IrField("omitted", text, IrPresence.OmittedWhenNull, [Column]),
            new IrField("kept", text, IrPresence.Always, [Column]),
            new IrField("never", text, IrPresence.OmittedWhenNull, []))), "ListBasketsRow");

        AssertSchema("""{ "type": "string" }""", row["properties"]!["omitted"]!);
        AssertSchema("""{ "type": ["string", "null"] }""", row["properties"]!["kept"]!);
        Assert.Equal(["kept", "never"], row["required"]!.AsArray().Select(k => (string?)k));
        Assert.Equal("Left out when null: nullable-column (shop.basket.note).", Description(row["properties"]!["omitted"]!));
        Assert.Equal("May be null: nullable-column (shop.basket.note).", Description(row["properties"]!["kept"]!));
    }

    [Theory]
    [InlineData(IrScalarKind.Boolean, null, """{ "type": "boolean" }""")]
    [InlineData(IrScalarKind.Date, null, """{ "type": "string", "format": "date", "pattern": "^\\d{4}-\\d{2}-\\d{2}$" }""")]
    [InlineData(IrScalarKind.Time, null, """{ "type": "string", "pattern": "^\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?$" }""")]
    [InlineData(IrScalarKind.Datetime, IrFormat.Local, """{ "type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?$" }""")]
    [InlineData(IrScalarKind.Datetime, IrFormat.Offset, """{ "type": "string", "format": "date-time", "pattern": "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d+)?(?:Z|[+-]\\d{2}:\\d{2})$" }""")]
    [InlineData(IrScalarKind.Datetime, null, """{ "type": "string" }""")]
    [InlineData(IrScalarKind.Uuid, null, """{ "type": "string", "format": "uuid", "pattern": "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$" }""")]
    [InlineData(IrScalarKind.Number, IrFormat.Uint8, """{ "type": "integer", "minimum": 0, "maximum": 255 }""")]
    [InlineData(IrScalarKind.Number, IrFormat.Int16, """{ "type": "integer", "minimum": -32768, "maximum": 32767 }""")]
    [InlineData(IrScalarKind.Number, IrFormat.Int64, """{ "type": "integer", "minimum": -9223372036854775808, "maximum": 9223372036854775807 }""")]
    [InlineData(IrScalarKind.Number, IrFormat.Float, """{ "type": "number" }""")]
    public void Describes_each_scalar_as_json_writes_it_in_a_rowset_and_in_json_alike(IrScalarKind kind, IrFormat? format, string expected)
    {
        AssertSchema(expected, Value(new IrScalar(kind, format, null), IrEncoding.Rowset));
        AssertSchema(expected, Value(new IrScalar(kind, format, null), IrEncoding.Json));
    }

    [Fact]
    public void A_bigint_says_it_is_exact_only_to_2_to_the_53_in_a_validator_of_doubles()
    {
        var value = Value(new IrScalar(IrScalarKind.Number, IrFormat.Int64, "bigint"));

        Assert.Contains("exact only up to 2^53", Description(value), StringComparison.Ordinal);
        Assert.Contains("integer up to 2^53", (string?)Emit(OneResult(IrEncoding.Rowset, Field("v", new IrScalar(IrScalarKind.Number, IrFormat.Int64, "bigint"))))["description"], StringComparison.Ordinal);
    }

    private static IrScalar Decimal(int precision, int scale, IrBound? min = null, IrBound? max = null, IrValueSet? values = null) =>
        new(IrScalarKind.Number, IrFormat.Decimal, $"decimal({precision},{scale})", new IrFacets(Digits: new IrDigits(precision, scale, new IrFactSource(IrFactSourceKind.Type, $"decimal({precision},{scale})")), Min: min, Max: max, Values: values));

    private static IrBound Bound(string value, bool inclusive) => new(value, inclusive, new IrFactSource(IrFactSourceKind.Check, "shop.ck_price"));

    [Fact]
    public void A_decimal_of_no_places_is_an_integer_and_one_of_places_is_a_number_without_multiple_of()
    {
        AssertSchema("""{ "type": "integer", "minimum": -99999, "maximum": 99999 }""", Value(Decimal(5, 0, Bound("-99999", true), Bound("99999", true))));

        var priced = Value(Decimal(5, 2, Bound("-999.99", true), Bound("999.99", true)));
        AssertSchema("""{ "type": "number", "minimum": -999.99, "maximum": 999.99 }""", priced);
        Assert.Contains("At most 2 decimal places, which multipleOf would check inexactly.", Description(priced), StringComparison.Ordinal);
    }

    [Fact]
    public void An_exclusive_bound_is_the_inclusive_one_a_step_inside_it()
    {
        AssertSchema("""{ "type": "number", "minimum": 0.01, "maximum": 99.99 }""", Value(Decimal(5, 2, Bound("0", false), Bound("100", false))));
        AssertSchema("""{ "type": "integer", "minimum": 1, "maximum": 2147483647 }""", Value(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Min: Bound("0", false)))));
    }

    [Fact]
    public void An_exclusive_bound_of_no_known_scale_is_written_inclusive_and_says_so()
    {
        var value = Value(new IrScalar(IrScalarKind.Number, IrFormat.Float, "float", new IrFacets(Min: Bound("0", false))));

        AssertSchema("""{ "type": "number", "minimum": 0 }""", value);
        Assert.Contains("Greater than 0, checked as at least it", Description(value), StringComparison.Ordinal);
    }

    [Fact]
    public void Bounds_past_a_double_are_written_exactly_and_say_so()
    {
        var text = JsonSchemaEmitter.Emit(OneResult(IrEncoding.Rowset, Field("v", Decimal(19, 2, Bound("-99999999999999999.99", true), Bound("99999999999999999.99", true))))).Expected();

        Assert.Contains("\"minimum\": -99999999999999999.99", text, StringComparison.Ordinal);
        Assert.Contains("decimal(19,2) has more digits than a double holds", text, StringComparison.Ordinal);
    }

    private static IrValueSet Set(IrValueMatch match, params string[] items) =>
        new([.. items.Select(i => new IrValue(i, IsString: true))], match, [new IrFactSource(IrFactSourceKind.Check, "shop.ck_status")], match == IrValueMatch.Canonical ? "Latin1_General_CI_AS" : null);

    private static IrScalar Text(IrValueSet? values = null, IrLength? length = null, IrMinLength? minLength = null) =>
        new(IrScalarKind.String, null, "varchar(10)", new IrFacets(Length: length, MinLength: minLength, Values: values));

    [Fact]
    public void An_exact_value_set_is_an_enum_and_null_joins_it()
    {
        var because = new[] { Column };
        var row = Definition(Emit(OneResult(
            IrEncoding.Rowset,
            Field("status", Text(Set(IrValueMatch.Exact, "open", "held"))),
            new IrField("maybe", Text(Set(IrValueMatch.Exact, "open")), IrPresence.Always, [.. because]),
            Field("none", Text(Set(IrValueMatch.Exact))))), "ListBasketsRow")["properties"]!;

        AssertSchema("""{ "enum": ["open", "held"] }""", row["status"]!);
        AssertSchema("""{ "enum": ["open", null] }""", row["maybe"]!);
        AssertSchema("""{ "not": {} }""", row["none"]!);
    }

    [Theory]
    [InlineData(IrValueMatch.Collation)]
    [InlineData(IrValueMatch.Canonical)]
    public void A_set_that_holds_as_the_database_compares_is_a_string_with_a_note(IrValueMatch match)
    {
        var value = Value(Text(Set(match, "open", "held")));

        AssertSchema("""{ "type": "string" }""", value);
        Assert.Contains("One of \"open\", \"held\", as the database compares strings", Description(value), StringComparison.Ordinal);
        Assert.Contains("Facts: values from the check shop.ck_status.", Description(value), StringComparison.Ordinal);
    }

    [Fact]
    public void A_padded_set_is_a_pattern_with_its_items_escaped()
    {
        var value = Value(Text(Set(IrValueMatch.Padded, "a.b", "c|d ")));

        AssertSchema("""{ "type": "string", "pattern": "^(?:a\\.b|c\\|d) *$" }""", value);
    }

    private static IrLength Length(int value, bool isFixed, IrLengthUnit unit, IrByteEncoding encoding = IrByteEncoding.Unknown) =>
        new(value, isFixed, unit, encoding, new IrFactSource(IrFactSourceKind.Type, "varchar(10)"));

    [Theory]
    // A maximum of n UTF-16 code units or bytes is at most n code points, which JSON Schema counts.
    [InlineData(10, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, """{ "type": "string", "maxLength": 10 }""")]
    [InlineData(10, false, IrLengthUnit.Bytes, IrByteEncoding.Utf8, """{ "type": "string", "maxLength": 10 }""")]
    // A fixed length is at least n over the most units a code point takes.
    [InlineData(10, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, """{ "type": "string", "minLength": 5, "maxLength": 10 }""")]
    [InlineData(10, true, IrLengthUnit.Bytes, IrByteEncoding.SingleByte, """{ "type": "string", "minLength": 10, "maxLength": 10 }""")]
    [InlineData(10, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8, """{ "type": "string", "minLength": 3, "maxLength": 10 }""")]
    [InlineData(10, true, IrLengthUnit.Bytes, IrByteEncoding.DoubleByte, """{ "type": "string", "minLength": 5, "maxLength": 10 }""")]
    public void A_length_is_counted_in_code_points(int value, bool isFixed, IrLengthUnit unit, IrByteEncoding encoding, string expected) =>
        AssertSchema(expected, Value(Text(length: Length(value, isFixed, unit, encoding))));

    [Fact]
    public void A_length_widened_into_code_points_says_how()
    {
        var utf8 = Value(Text(length: Length(10, true, IrLengthUnit.Bytes, IrByteEncoding.Utf8)));
        var minimum = Value(Text(minLength: new IrMinLength(4, new IrFactSource(IrFactSourceKind.Check, "shop.ck_code"))));

        Assert.Contains("varchar(10) pads to 10 bytes and UTF-8 takes up to four a character, so the length is 3 to 10 code points.", Description(utf8), StringComparison.Ordinal);
        AssertSchema("""{ "type": "string", "minLength": 2 }""", minimum);
        Assert.Contains("At least 4 UTF-16 code units, which is at least 2 code points.", Description(minimum), StringComparison.Ordinal);
    }

    [Fact]
    public void An_array_carries_its_items_nullability_and_length()
    {
        var value = Value(new IrArray(new IrScalar(IrScalarKind.Boolean, null, "bit")) { ItemsNullableBecause = [Column], MinItems = 1, MaxItems = 3 });

        AssertSchema("""{ "type": "array", "items": { "type": ["boolean", "null"] }, "minItems": 1, "maxItems": 3 }""", value);
    }

    [Fact]
    public void A_tuple_is_its_positions_and_no_more()
    {
        var value = Value(new IrTuple([new IrElement(new IrScalar(IrScalarKind.Boolean, null, null), []), new IrElement(new IrScalar(IrScalarKind.String, null, null), [Column])]));

        AssertSchema("""{ "type": "array", "prefixItems": [{ "type": "boolean" }, { "type": ["string", "null"] }], "items": false, "minItems": 2 }""", value);
    }

    [Fact]
    public void A_record_is_an_object_whose_every_value_has_one_schema() =>
        AssertSchema("""{ "type": "object", "additionalProperties": { "type": "boolean" } }""", Value(new IrRecord(new IrScalar(IrScalarKind.Boolean, null, null), [])));

    [Fact]
    public void A_union_is_any_of_its_variants_each_written_once()
    {
        var text = new IrScalar(IrScalarKind.String, null, null);
        var value = Value(new IrUnion([text, new IrScalar(IrScalarKind.Boolean, null, null), text]));

        AssertSchema("""{ "anyOf": [{ "type": "string" }, { "type": "boolean" }] }""", value);
    }

    [Fact]
    public void A_nullable_reference_is_any_of_it_and_null()
    {
        var document = OneResult(IrEncoding.Json, new IrField("basket", new IrRef("Basket"), IrPresence.Always, [Column]));
        document = document with { Types = new Dictionary<string, IrObjectType>(document.Types) { ["Basket"] = new([Field("id", new IrScalar(IrScalarKind.Boolean, null, null))]) } };

        var basket = Definition(Emit(document), "ListBasketsRow")["properties"]!["basket"]!;

        AssertSchema("""{ "anyOf": [{ "$ref": "#/$defs/Basket" }, { "type": "null" }] }""", basket);
    }

    [Fact]
    public void A_recursive_type_refers_to_itself()
    {
        var document = OneResult(IrEncoding.Json, Field("children", new IrArray(new IrRef("ListBasketsRow"))));

        AssertSchema("""{ "type": "array", "items": { "$ref": "#/$defs/ListBasketsRow" } }""", Definition(Emit(document), "ListBasketsRow")["properties"]!["children"]!);
    }

    [Fact]
    public void Json_text_of_known_content_has_its_media_type_and_content_schema()
    {
        var known = Value(new IrJsonText(new IrScalar(IrScalarKind.Boolean, null, null)));
        var unknown = Value(new IrJsonText());

        AssertSchema("""{ "type": "string", "contentMediaType": "application/json", "contentSchema": { "type": "boolean" } }""", known);
        Assert.Contains("contentSchema describes", Description(known), StringComparison.Ordinal);
        AssertSchema("""{ "type": "string", "contentMediaType": "application/json" }""", unknown);
    }

    [Fact]
    public void An_unknown_accepts_anything_and_gives_the_irs_reason_for_its_place()
    {
        var document = OneResult(IrEncoding.Rowset, Field("v", new IrUnknownType(IrUnknownReason.UnsupportedExpression)));
        document = document with
        {
            Types = new Dictionary<string, IrObjectType> { ["ListBasketsRow"] = document.Types["ListBasketsRow"] with { Identity = new IrTypeIdentity(IrTypeOwner.Procedure, "shop.list_baskets", "results[0][]") } },
            Unknowns = [new IrUnknown("shop.list_baskets", "results[0][].v", IrUnknownReason.UnsupportedExpression, "The expression upper(b.note) is not modelled yet.", "p.sql:3:8")],
        };

        var value = Definition(Emit(document), "ListBasketsRow")["properties"]!["v"]!;

        AssertSchema("{}", value);
        Assert.Equal("Not known (unsupported-expression): The expression upper(b.note) is not modelled yet.", Description(value));
    }

    [Fact]
    public void An_imported_type_accepts_anything_and_names_the_schema_it_was_declared()
    {
        var value = Value(new IrImport("BasketSettingsSchema", "@shop/types"));

        AssertSchema("{}", value);
        Assert.Contains("BasketSettingsSchema from @shop/types", Description(value), StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_objects_forbid_other_keys_and_strip_ones_allow_them()
    {
        var document = OneResult(IrEncoding.Json, Field("id", new IrScalar(IrScalarKind.Boolean, null, null)));

        Assert.Equal(false, (bool?)Definition(Emit(document, new JsonSchemaOptions { Objects = JsonSchemaObjects.Strict }), "ListBasketsRow")["additionalProperties"]);
        Assert.Null(Definition(Emit(document), "ListBasketsRow")["additionalProperties"]);
    }

    private static IrDocument Results(params IrResult[] results) => new(
        new IrSource("tsql"),
        [new IrObject("shop.get_basket", "GetBasket", IrObjectKind.Procedure, "p.sql:1:1", [], [.. results])],
        new Dictionary<string, IrObjectType> { ["Basket"] = new([Field("id", new IrScalar(IrScalarKind.Boolean, null, null))]) },
        []);

    private static readonly IrNullReason NoRows = new(IrNullRule.NoRows, "p.sql:2:1");

    [Fact]
    public void An_absent_json_result_reads_as_null_and_one_that_arrives_has_an_item()
    {
        var getBasket = Definition(Emit(Results(new IrResult(IrEncoding.Json, new IrArray(new IrRef("Basket")), [NoRows]) { Cardinality = IrCardinality.AtMostOne })), "GetBasket");

        AssertSchema("""{ "title": "shop.get_basket", "type": ["array", "null"], "items": { "$ref": "#/$defs/Basket" }, "minItems": 1, "maxItems": 1 }""", getBasket);
        Assert.StartsWith("Returns JSON, null when no rows match.", Description(getBasket), StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_object_result_reads_as_null() =>
        AssertSchema(
            """{ "title": "shop.get_basket", "anyOf": [{ "$ref": "#/$defs/Basket" }, { "type": "null" }] }""",
            Definition(Emit(Results(new IrResult(IrEncoding.Json, new IrRef("Basket"), [NoRows]))), "GetBasket"));

    [Fact]
    public void An_absent_rowset_reads_as_no_rows_so_is_bounded_only_above() =>
        AssertSchema(
            """{ "title": "shop.get_basket", "type": "array", "items": { "$ref": "#/$defs/Basket" }, "maxItems": 1 }""",
            Definition(Emit(Results(new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Basket")), [new IrNullReason(IrNullRule.NotRun, "p.sql:3:1")]) { Cardinality = IrCardinality.One })), "GetBasket"));

    [Fact]
    public void Several_results_are_an_array_of_exactly_that_many()
    {
        var rows = new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Basket")), []);

        AssertSchema(
            """{ "title": "shop.get_basket", "type": "array", "prefixItems": [{ "type": "array", "items": { "$ref": "#/$defs/Basket" } }, { "type": "array", "items": { "$ref": "#/$defs/Basket" } }], "items": false, "minItems": 2 }""",
            Definition(Emit(Results(rows, rows)), "GetBasket"));
    }

    [Fact]
    public void No_results_is_the_empty_array() =>
        AssertSchema("""{ "title": "shop.get_basket", "type": "array", "maxItems": 0 }""", Definition(Emit(Results()), "GetBasket"));

    [Fact]
    public void Results_that_cannot_be_listed_accept_anything_and_say_why()
    {
        var document = new IrDocument(
            new IrSource("tsql"),
            [new IrObject("shop.run_report", "RunReport", IrObjectKind.Procedure, "p.sql:1:1", [], null)],
            new Dictionary<string, IrObjectType>(),
            [new IrUnknown("shop.run_report", "results", IrUnknownReason.DynamicSql, "The procedure runs SQL built as a string.", "p.sql:4:5")]);

        var runReport = Definition(Emit(document), "RunReport");

        AssertSchema("""{ "title": "shop.run_report" }""", runReport);
        Assert.Equal("Its result sets can't be listed (dynamic-sql): The procedure runs SQL built as a string.", Description(runReport));
    }

    [Fact]
    public void Refuses_one_name_for_a_procedure_and_a_type()
    {
        var document = Results() with { Types = new Dictionary<string, IrObjectType> { ["GetBasket"] = new([]) } };

        var emitted = JsonSchemaEmitter.Emit(document);

        var refusal = Assert.Single(Assert.IsType<Failed<string>>(emitted).Diagnostics);
        Assert.Equal(JsonSchemaCodes.NamedTwice, refusal.Code);
        Assert.Contains("'GetBasket' names a type and the procedure shop.get_basket", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Facts_from_elsewhere_than_the_sql_type_say_where_they_came_from()
    {
        var value = Value(new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int", new IrFacets(Min: Bound("0", true), Max: Bound("10", true))));

        Assert.Equal("SQL type int. Facts: min and max from the check shop.ck_price.", Description(value));
    }

    [Fact]
    public void A_named_type_says_where_it_comes_from()
    {
        var document = OneResult(IrEncoding.Rowset, Field("id", new IrScalar(IrScalarKind.Boolean, null, null)));
        document = document with { Types = new Dictionary<string, IrObjectType> { ["ListBasketsRow"] = document.Types["ListBasketsRow"] with { Identity = new IrTypeIdentity(IrTypeOwner.Procedure, "shop.list_baskets", "results[0][]") } } };

        Assert.Equal("The type at results[0][] in shop.list_baskets.", Description(Definition(Emit(document), "ListBasketsRow")));
    }

    [Fact]
    public void A_declared_session_is_said_in_the_documents_description()
    {
        var document = OneResult(IrEncoding.Rowset) with { Source = new IrSource("tsql") { Session = [new IrSessionOption("ansiWarnings", true)] } };

        Assert.Contains("callers' sessions set ansiWarnings on", (string?)Emit(document)["description"], StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_the_same_bytes_every_time_with_lf_line_endings_and_a_final_newline()
    {
        var document = OneResult(IrEncoding.Json, Field("é", new IrScalar(IrScalarKind.String, null, "nvarchar(10)")));

        var first = JsonSchemaEmitter.Emit(document).Expected();

        Assert.Equal(first, JsonSchemaEmitter.Emit(document).Expected());
        Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
        Assert.EndsWith("}\n", first, StringComparison.Ordinal);
        Assert.Contains("\"é\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public void A_float_value_set_is_a_note_since_its_json_may_not_read_back_as_the_literal()
    {
        var floats = new IrValueSet([new IrValue("0.1", IsString: false)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Expression, "p.sql:2:8")]);

        var value = Value(new IrScalar(IrScalarKind.Number, IrFormat.Float, "float", new IrFacets(Values: floats)));

        AssertSchema("""{ "type": "number" }""", value);
        Assert.Contains("One of 0.1", Description(value), StringComparison.Ordinal);
    }

    [Fact]
    public void An_exact_set_of_numbers_is_an_enum_of_their_exact_text()
    {
        var set = new IrValueSet([new IrValue("1.50", IsString: false), new IrValue("2", IsString: false)], IrValueMatch.Exact, EquatableArray.Create<IrFactSource>([]));

        AssertSchema("""{ "enum": [1.50, 2] }""", Value(Decimal(5, 2, values: set)));
    }
}
