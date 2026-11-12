using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// The validator and the coverage check on the nodes the JSON constructors produce (IR 0.3): tuples, records, unions,
/// and arrays with nullable items and lengths. As in <see cref="IrValidatorTests"/>, each claim has a case that keeps
/// it and one that breaks it.
/// </summary>
public class JsonNodeValidationTests
{
    private static readonly IrNullReason Because = new(IrNullRule.NullableColumn, "dbo.t.c");
    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");
    private static readonly IrScalar Text = new(IrScalarKind.String, null, "nvarchar(10)");

    private static IrDocument Doc(IrType type, params (string Name, IrField[] Fields)[] more) => new(
        new IrSource("tsql"),
        [new IrObject("dbo.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrRef("Row"), [])])],
        new[] { ("Row", new[] { new IrField("v", type, IrPresence.Always, []) }) }.Concat(more).ToDictionary(t => t.Item1, t => new IrObjectType([.. t.Item2])),
        []);

    private static ValidationReport Validate(IrType type, string value, params (string Name, IrField[] Fields)[] more) =>
        IrValidator.Validate(Doc(type, more), "dbo.p", [new ObservedResultSet(["json"], [[$$"""{"v":{{value}}}"""]])]);

    private static void Valid(IrType type, string value, params (string Name, IrField[] Fields)[] more) =>
        Assert.Empty(Validate(type, value, more).Violations);

    private static Violation Invalid(IrType type, string value, params (string Name, IrField[] Fields)[] more) =>
        Assert.Single(Validate(type, value, more).Violations);

    // ---- tuples --------------------------------------------------------------------------------------

    private static readonly IrTuple Pair = new([new IrElement(Int, []), new IrElement(Text, [Because])]);

    [Fact]
    public void A_tuple_has_exactly_its_elements_each_of_its_type()
    {
        Valid(Pair, """[1,"a"]""");
        Valid(Pair, "[1,null]");
        Assert.Contains("2 elements", Invalid(Pair, "[1]").Claim, StringComparison.Ordinal);
        Assert.Contains("2 elements", Invalid(Pair, """[1,"a",2]""").Claim, StringComparison.Ordinal);
        Assert.Equal("results[0].v[0]", Invalid(Pair, """["x","a"]""").Path);
        Assert.Equal("results[0].v[0]", Invalid(Pair, "[null,null]").Path);
        Assert.Contains("an array", Invalid(Pair, "{}").Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tuple_records_what_each_position_held()
    {
        var seen = Validate(Pair, "[1,null]").Seen;

        Assert.Contains(("results[0].v[0]", Seen.Value), seen);
        Assert.Contains(("results[0].v[1]", Seen.Null), seen);
    }

    // ---- arrays --------------------------------------------------------------------------------------

    [Fact]
    public void An_array_holds_nulls_only_when_its_items_may_be_null()
    {
        Valid(new IrArray(Int) { ItemsNullableBecause = [Because] }, "[1,null]");
        Assert.Equal("results[0].v[1]", Invalid(new IrArray(Int), "[1,null]").Path);
        Assert.Contains(("results[0].v[]", Seen.Null), Validate(new IrArray(Int) { ItemsNullableBecause = [Because] }, "[null]").Seen);
    }

    [Fact]
    public void An_array_keeps_its_length_and_records_reaching_it()
    {
        var bounded = new IrArray(Int) { MinItems = 1, MaxItems = 2 };

        Valid(bounded, "[1]");
        Assert.Contains("at least 1 item", Invalid(bounded, "[]").Claim, StringComparison.Ordinal);
        Assert.Contains("at most 2 items", Invalid(bounded, "[1,2,3]").Claim, StringComparison.Ordinal);
        Assert.Contains(("results[0].v", "min items 1"), Validate(bounded, "[1]").Edges);
        Assert.Contains(("results[0].v", "max items 2"), Validate(bounded, "[1,2]").Edges);
    }

    // ---- records -------------------------------------------------------------------------------------

    [Fact]
    public void A_record_has_any_keys_even_repeated_and_values_of_one_type()
    {
        var record = new IrRecord(Int, []);

        Valid(record, "{}");
        // json_objectagg writes a repeated key each time (observed); a record claims nothing about uniqueness.
        Valid(record, """{"a":1,"a":2,"b":3}""");
        Assert.Equal("results[0].v.b", Invalid(record, """{"a":1,"b":"x"}""").Path);
        Assert.Contains("a non-null value", Invalid(record, """{"a":null}""").Claim, StringComparison.Ordinal);
        Valid(new IrRecord(Int, [Because]), """{"a":null}""");
        Assert.Contains("an object", Invalid(record, "[]").Claim, StringComparison.Ordinal);
        Assert.Contains(("results[0].v{}", Seen.Null), Validate(new IrRecord(Int, [Because]), """{"a":null}""").Seen);
        Assert.Contains(("results[0].v{}", Seen.Value), Validate(record, """{"a":1}""").Seen);
    }

    // ---- unions --------------------------------------------------------------------------------------

    [Fact]
    public void A_union_value_matches_one_of_its_variants()
    {
        var union = new IrUnion([Int, Text]);

        Valid(new IrArray(union), """[1,"a"]""");
        Assert.Contains("one of", Invalid(new IrArray(union), "[true]").Claim, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_of_several_variants_reaches_the_edges_of_each()
    {
        var from = new IrFactSource(IrFactSourceKind.Type, "t");
        var union = new IrUnion([
            new IrScalar(IrScalarKind.String, null, "nvarchar(60)", new IrFacets(Length: new IrLength(60, false, IrLengthUnit.Utf16, IrByteEncoding.Unknown, from))),
            new IrScalar(IrScalarKind.String, null, "nchar(3)", new IrFacets(Length: new IrLength(3, true, IrLengthUnit.Utf16, IrByteEncoding.Unknown, from))),
        ]);

        Assert.Contains(("results[0].v", "length 3"), Validate(union, "\"abc\"").Edges);
    }

    [Fact]
    public void A_union_of_objects_records_what_the_matching_variant_saw()
    {
        var union = new IrUnion([new IrRef("A"), new IrRef("B")]);
        (string, IrField[]) a = ("A", [new IrField("a", Int, IrPresence.Always, [])]);
        (string, IrField[]) b = ("B", [new IrField("b", Text, IrPresence.Always, [])]);

        var report = Validate(union, """{"b":"x"}""", a, b);

        Assert.Empty(report.Violations);
        Assert.Contains(("results[0].v.b", Seen.Value), report.Seen);
        Assert.DoesNotContain(("results[0].v.a", Seen.Value), report.Seen);
        Assert.Single(Validate(union, """{"c":1}""", a, b).Violations);
    }

    // ---- coverage ------------------------------------------------------------------------------------

    [Fact]
    public void Coverage_needs_each_position_record_value_and_nullable_item_seen_both_ways()
    {
        var doc = Doc(new IrTuple([
            new IrElement(Pair, []),
            new IrElement(new IrRecord(Int, [Because]), []),
            new IrElement(new IrArray(Int) { ItemsNullableBecause = [Because] }, []),
            new IrElement(new IrUnion([Int, new IrRecord(Text, [])]), []),
            new IrElement(new IrUnknownType(IrUnknownReason.JsonShapeUnknown), [Because]),
        ]));

        Assert.Equal(
            [
                "dbo.p results[0].v: a value",
                "dbo.p results[0].v[0]: a value",
                "dbo.p results[0].v[0][0]: a value",
                "dbo.p results[0].v[0][1]: a value",
                "dbo.p results[0].v[0][1]: null",
                "dbo.p results[0].v[1]: a value",
                "dbo.p results[0].v[1]{}: a value",
                "dbo.p results[0].v[1]{}: null",
                "dbo.p results[0].v[2]: a value",
                "dbo.p results[0].v[2][]: a value",
                "dbo.p results[0].v[2][]: null",
                "dbo.p results[0].v[3]: a value",
                "dbo.p results[0].v[3]{}: a value",
                "dbo.p results[0]: a value",
            ],
            Coverage.Missing(doc, new Dictionary<string, IReadOnlySet<(string, Seen)>>()));
    }

    [Fact]
    public void Edge_coverage_needs_an_array_s_length_bounds_reached()
    {
        var doc = Doc(new IrArray(Int) { MinItems = 1, MaxItems = 3 });

        Assert.Equal(
            ["dbo.p results[0].v: max items 3", "dbo.p results[0].v: min items 1"],
            Coverage.MissingEdges(doc, new Dictionary<string, IReadOnlySet<(string, string)>>()));
    }
}
