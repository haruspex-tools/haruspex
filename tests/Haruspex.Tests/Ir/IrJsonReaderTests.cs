using System.Text.Json.Nodes;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// <see cref="IrJsonReader"/> on a document built by hand that holds every node and every optional property of the format,
/// and on that document's file broken one way at a time: each refusal names the path, and nothing is read with a default.
/// The round trip over real output (every fixture, both corpora) is in <c>IrRoundTripTests</c>.
/// </summary>
public class IrJsonReaderTests
{
    /// <summary>The document's JSON with <paramref name="change"/> made to it, read: the refusal the reader gives.</summary>
    private static IrFormatException Refused(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(IrJsonWriter.Write(IrSamples.Everything))!.AsObject();
        change(json);
        return Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json.ToJsonString()));
    }

    private static JsonObject FirstField(JsonObject json) => json["types"]!["OrderRow"]!["fields"]![0]!.AsObject();

    private static JsonObject FirstResult(JsonObject json) => json["objects"]![0]!["results"]![0]!.AsObject();

    [Fact]
    public void A_document_with_every_node_and_property_reads_back_equal()
    {
        var json = IrJsonWriter.Write(IrSamples.Everything);

        var read = IrJsonReader.Read(json);

        Assert.Equal(IrSamples.Everything, read);
        Assert.Equal(json, IrJsonWriter.Write(read));
    }

    [Fact]
    public void The_sample_holds_every_type_node_the_format_has()
    {
        var nodes = IrSamples.TypeNodes(IrSamples.Everything).Select(t => t.GetType().Name).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(typeof(IrType).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(IrType)) && !t.IsAbstract).Select(t => t.Name).Order(StringComparer.Ordinal), nodes);
    }

    [Fact]
    public void A_document_read_equals_one_whose_sorted_lists_were_in_another_order()
    {
        var shuffled = IrSamples.Everything with
        {
            Objects = [.. IrSamples.Everything.Objects.Reverse()],
            Unknowns = [.. IrSamples.Everything.Unknowns.Reverse()],
            Overrides = [.. IrSamples.Everything.Overrides.Reverse()],
        };

        Assert.Equal(shuffled, IrJsonReader.Read(IrJsonWriter.Write(IrSamples.Everything)));
        Assert.Equal(IrJsonWriter.Write(IrSamples.Everything), IrJsonWriter.Write(shuffled));
    }

    [Fact]
    public void A_document_that_differs_in_one_field_is_not_equal()
    {
        var other = IrSamples.Everything with { Types = new Dictionary<string, IrObjectType>(IrSamples.Everything.Types) { ["OrderRow"] = new([]) } };

        Assert.NotEqual(IrSamples.Everything, other);
    }

    [Fact]
    public void Text_that_is_not_json_is_refused_naming_where_the_parser_stopped()
    {
        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read("{\n  \"irVersion\": \"0.15\",\n  \"source\": }"));

        Assert.Equal("$", e.Path);
        Assert.StartsWith("is not JSON at line 3, byte 13: ", e.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_that_is_not_an_object_is_refused()
    {
        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read("[]"));

        Assert.Equal("$ must be an object.", e.Message);
    }

    [Fact]
    public void A_file_without_a_version_is_refused_as_not_an_ir_file()
    {
        var e = Refused(json => json.Remove("irVersion"));

        Assert.Equal("$.irVersion", e.Path);
        Assert.Contains("not a haruspex IR file", e.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_session_is_refused_since_none_declared_is_written_by_leaving_it_out()
    {
        var e = Refused(json => json["source"]!["session"] = new JsonObject());

        Assert.Equal("$.source.session", e.Path);
    }

    [Fact]
    public void A_session_option_that_is_not_true_or_false_is_refused()
    {
        var e = Refused(json => json["source"]!["session"] = new JsonObject { ["ansiWarnings"] = "on" });

        Assert.Equal("$.source.session.ansiWarnings", e.Path);
    }

    [Fact]
    public void Another_version_is_refused_naming_it_and_the_versions_read()
    {
        var e = Refused(json =>
        {
            json["irVersion"] = "0.14";
            json["objects"] = "anything at all";
        });

        Assert.Equal("$.irVersion", e.Path);
        Assert.Equal("is \"0.14\", and this reader reads IR 0.16 and 0.17 only. Run haruspex generate again to write the file in this version.", e.Problem);
    }

    [Fact]
    public void A_missing_required_property_is_refused_by_its_path()
    {
        var e = Refused(json => FirstResult(json).Remove("cardinality"));

        Assert.Equal("$.objects[0].results[0].cardinality is missing.", e.Message);
    }

    [Fact]
    public void A_property_of_the_wrong_type_is_refused_by_its_path()
    {
        var e = Refused(json => json["objects"]![0]!["parameters"]![0]!["hasDefault"] = "no");

        Assert.Equal("$.objects[0].parameters[0].hasDefault must be true or false.", e.Message);
    }

    [Fact]
    public void An_unknown_property_is_refused_rather_than_ignored()
    {
        var e = Refused(json => FirstField(json)["colour"] = "red");

        Assert.Equal("$.types.OrderRow.fields[0].colour is not a property the format has here.", e.Message);
    }

    [Fact]
    public void A_property_given_twice_is_refused()
    {
        var json = IrJsonWriter.Write(IrSamples.Everything).Replace("\"dialect\": \"tsql\"", "\"dialect\": \"tsql\", \"dialect\": \"other\"", StringComparison.Ordinal);

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json));

        Assert.Equal("$.source.dialect appears twice.", e.Message);
    }

    [Fact]
    public void An_unknown_null_rule_is_refused_naming_its_vocabulary()
    {
        var e = Refused(json => FirstField(json)["nullableBecause"]![0]!["rule"] = "outer-applied");

        Assert.Equal($"$.types.OrderRow.fields[0].nullableBecause[0].rule is \"outer-applied\", which is not a null rule in IR {IrDocument.CurrentVersion}.", e.Message);
    }

    [Fact]
    public void An_unknown_type_kind_is_refused()
    {
        var e = Refused(json => FirstField(json)["type"]!["kind"] = "money");

        Assert.Equal($"$.types.OrderRow.fields[0].type.kind is \"money\", which is not a type in IR {IrDocument.CurrentVersion}.", e.Message);
    }

    [Fact]
    public void A_named_type_that_is_not_an_object_is_refused()
    {
        var e = Refused(json => json["types"]!["OrderRow"]!["kind"] = "record");

        Assert.Equal("$.types.OrderRow.kind", e.Path);
    }

    [Fact]
    public void A_nullable_field_without_reasons_is_refused_not_read_as_never_null()
    {
        var e = Refused(json => FirstField(json).Remove("nullableBecause"));

        Assert.Equal("$.types.OrderRow.fields[0].nullableBecause is missing, but nullable is true: every null needs a reason.", e.Message);
    }

    [Fact]
    public void Reasons_given_for_a_field_never_null_are_refused()
    {
        var e = Refused(json => FirstField(json)["nullable"] = false);

        Assert.Equal("$.types.OrderRow.fields[0].nullableBecause", e.Path);
    }

    [Fact]
    public void An_empty_list_of_reasons_is_refused()
    {
        var e = Refused(json => FirstField(json)["nullableBecause"] = new JsonArray());

        Assert.Equal("$.types.OrderRow.fields[0].nullableBecause", e.Path);
    }

    [Fact]
    public void A_number_written_with_an_exponent_is_refused_rather_than_rounded()
    {
        var json = IrJsonWriter.Write(IrSamples.Everything).Replace("\"value\": 99999.99", "\"value\": 9.999999e4", StringComparison.Ordinal);

        var e = Assert.Throws<IrFormatException>(() => IrJsonReader.Read(json));

        Assert.EndsWith(".max.value", e.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fact_source_of_an_unknown_kind_is_refused()
    {
        var e = Refused(json => FirstField(json)["type"]!["maxLength"]!["from"] = new JsonObject { ["guess"] = "x" });

        Assert.Equal("$.types.OrderRow.fields[0].type.maxLength.from.guess", e.Path);
    }

    [Fact]
    public void A_key_that_is_not_an_identifier_is_quoted_in_the_path()
    {
        var e = Refused(json => json["types"]!["OrderRow"]!["fields"]![0]!["type"]!.AsObject().Add("x y", 1));

        Assert.Equal("$.types.OrderRow.fields[0].type[\"x y\"]", e.Path);
    }

    [Fact]
    public void A_reference_to_no_type_is_refused_where_it_is()
    {
        var e = Refused(json => json["types"]!.AsObject().Remove("LineRow"));

        Assert.Equal("$.objects[0].results[1].type.items.ref is \"LineRow\", which names no type in $.types.", e.Message);
    }

    [Fact]
    public void Results_that_cannot_be_listed_read_as_null_not_as_none()
    {
        var unlisted = IrSamples.Everything with
        {
            Objects = [.. IrSamples.Everything.Objects.Select(o => o with { Results = null })],
        };

        var read = IrJsonReader.Read(IrJsonWriter.Write(unlisted));

        Assert.All(read.Objects, o => Assert.Null(o.Results));
    }
}
