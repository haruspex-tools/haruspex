using System.Globalization;
using Haruspex.Emit.Zod;
using Haruspex.Emit.Zod.Lowering;
using Haruspex.Emit.Zod.Model;
using Haruspex.Emit.Zod.Planning;
using Haruspex.Ir;
using static Haruspex.Emit.Zod.Model.ZodExpression;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>A plan lowered to declarations: object types with their getters, and procedures with their results and summaries.</summary>
public class ZodLoweringTests
{
    private static readonly IrScalar Flag = new(IrScalarKind.Boolean, null, "bit");
    private static readonly IrNullReason NoRows = new(IrNullRule.NoRows, "p.sql:2:1");

    private static IrField Field(string name, IrType type) => new(name, type, IrPresence.Always, []);

    private static ZodModule Lower(IrDocument document, ZodOptions? options = null) => ZodLowering.Lower(ZodPlanner.Plan(document, options ?? new ZodOptions()).Expected());

    private static IrDocument Document(Dictionary<string, IrObjectType> types, params IrResult[]? results) => new(
        new IrSource("tsql"),
        [new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], results is null ? null : [.. results])],
        types,
        []);

    private static ZodDeclaration Procedure(ZodModule module) => Assert.Single(module.Declarations, d => d.Name == "P");

    [Fact]
    public void Types_of_the_same_shape_are_one_unexported_shape_named_by_its_structure_and_each_exported_under_its_own_name()
    {
        var module = Lower(Document(
            new()
            {
                ["A"] = new([Field("flag", Flag)]),
                ["B"] = new([Field("flag", Flag)]),
                ["UsesA"] = new([Field("item", new IrRef("A"))]),
                ["UsesB"] = new([Field("item", new IrRef("B"))]),
            },
            new IrResult(IrEncoding.Json, new IrTuple([new IrElement(new IrRef("UsesA"), []), new IrElement(new IrRef("UsesB"), [])]), [])));

        // The inner shape's name is in the outer one's text, so the outer one's hash follows from the inner one's.
        var flag = Shape("z.object({\n  flag: z.boolean(),\n})");
        var item = Shape($"z.object({{\n  item: {flag},\n}})");
        Assert.Equal(
            [
                new ZodDeclaration(flag, Shared, new ObjectSchema([new ZodProperty("flag", Z("boolean"))], Strict: false), Exported: false),
                new ZodDeclaration("A", null, new Name(flag)),
                new ZodDeclaration(item, Shared, new ObjectSchema([new ZodProperty("item", new Name(flag))], Strict: false), Exported: false),
                new ZodDeclaration("UsesA", null, new Name(item)),
                new ZodDeclaration("B", null, new Name(flag)),
                new ZodDeclaration("UsesB", null, new Name(item)),
            ],
            module.Declarations.Where(d => d.Name != "P"));

        // Outside a shared shape, a reference names its own type's schema.
        Assert.Equal(Z("tuple", new ArrayLiteral([new Name("UsesASchema"), new Name("UsesBSchema")])), Procedure(module).Schema);
    }

    [Fact]
    public void A_shared_shape_is_declared_under_a_name_the_config_file_gave_one_of_its_types_the_ordinally_first()
    {
        var module = Lower(Document(
            new()
            {
                ["A"] = new([Field("flag", Flag)]),
                ["Total"] = new([Field("flag", Flag)]) { NameDeclared = true },
                ["Sum"] = new([Field("flag", Flag)]) { NameDeclared = true },
                ["UsesA"] = new([Field("a", new IrRef("A")), Field("total", new IrRef("Total")), Field("sum", new IrRef("Sum"))]),
            },
            new IrResult(IrEncoding.Json, new IrRef("UsesA"), [])));

        Assert.Equal(
            [
                new ZodDeclaration("Sum", Shared, new ObjectSchema([new ZodProperty("flag", Z("boolean"))], Strict: false)),
                new ZodDeclaration("A", null, new Name("SumSchema")),
                new ZodDeclaration("Total", null, new Name("SumSchema")),
            ],
            module.Declarations.Where(d => d.Name is "A" or "Sum" or "Total"));
    }

    [Fact]
    public void A_shape_s_name_depends_on_its_structure_not_on_which_types_share_it_or_their_order()
    {
        static IrDocument Sharing(params string[] names) => Document(
            names.ToDictionary(n => n, _ => new IrObjectType([Field("flag", Flag)]), StringComparer.Ordinal),
            new IrResult(IrEncoding.Json, new IrTuple([.. names.Select(n => new IrElement(new IrRef(n), []))]), []));

        var two = Lower(Sharing("A", "B")).Declarations.Single(d => !d.Exported);
        var three = Lower(Sharing("Zed", "C", "B")).Declarations.Single(d => !d.Exported);

        Assert.Equal(two, three);
    }

    [Fact]
    public void Two_shapes_whose_short_names_collide_are_given_longer_names_rather_than_one()
    {
        var names = new ShapeNames([], digits: 0);

        var first = names.Of("z.object({ a: z.boolean() })");
        var second = names.Of("z.object({ b: z.boolean() })");

        Assert.Equal("shape", first);
        Assert.StartsWith("shape", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(first, names.Of("z.object({ a: z.boolean() })"));
    }

    [Fact]
    public void A_shape_never_takes_a_name_the_module_imports()
    {
        var text = "z.object({ a: z.boolean() })";
        var usual = new ShapeNames([]).Of(text);

        Assert.NotEqual(usual, new ShapeNames([usual]).Of(text));
        Assert.StartsWith(usual, new ShapeNames([usual]).Of(text), StringComparison.Ordinal);
    }

    private const string Shared = "A shape several types share, declared once; each is exported under its own name.";

    /// <summary>The name of the shape the module writes as <paramref name="text"/>: <c>shape</c> and six digits of its SHA-256.</summary>
    private static string Shape(string text) => "shape" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..6];

    [Fact]
    public void The_header_states_once_each_shared_fact_the_notes_rest_on()
    {
        var bytes = new IrLength(8, false, IrLengthUnit.Bytes, IrByteEncoding.Unknown, new IrFactSource(IrFactSourceKind.Type, "varchar(8)"));
        var code = new IrScalar(IrScalarKind.String, null, "varchar(8)", new IrFacets(Length: bytes));
        var header = Lower(Document(new() { ["Row"] = new([Field("a", code), Field("b", code)]) }, new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), []))).Header;

        Assert.Equal(["", "The notes on properties below rest on these facts:"], header.Skip(2).Take(2));
        Assert.Single(header, line => line.StartsWith("- varchar(n) and char(n) count bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void The_header_states_the_facts_a_declarations_own_notes_rest_on_too()
    {
        var bigint = new IrScalar(IrScalarKind.Number, IrFormat.Int64, "bigint");
        var module = Lower(Document([], new IrResult(IrEncoding.Json, bigint, [])));

        Assert.Single(module.Header, line => line.StartsWith("- JavaScript reads every number", StringComparison.Ordinal));
    }

    [Fact]
    public void The_header_names_the_ir_version_and_the_rowset_mapping() =>
        Assert.Equal(
            [$"Generated by haruspex from IR {IrDocument.CurrentVersion}. Do not edit.", "Rowset results follow the default type mapping of the mssql (tedious) driver."],
            Lower(Document([], [])).Header);

    [Fact]
    public void The_header_states_what_the_callers_sessions_are_declared_to_set()
    {
        var declared = Document([], []) with
        {
            Source = new IrSource("tsql") { Session = [new IrSessionOption("ansiWarnings", true), new IrSessionOption("concatNullYieldsNull", false)] },
        };
        var one = Document([], []) with { Source = new IrSource("tsql") { Session = [new IrSessionOption("arithAbort", true)] } };
        var rounding = Document([], []) with
        {
            Source = new IrSource("tsql") { Session = [new IrSessionOption("ansiWarnings", true), new IrSessionOption("arithAbort", true), new IrSessionOption("numericRoundAbort", false)] },
        };

        Assert.Equal(
            "Its types assume that callers' sessions set ansiWarnings on and concatNullYieldsNull off, as the config file declares.",
            Lower(declared).Header[2]);
        Assert.Equal("Its types assume that callers' sessions set arithAbort on, as the config file declares.", Lower(one).Header[2]);
        Assert.Equal(
            "Its types assume that callers' sessions set ansiWarnings on, arithAbort on and numericRoundAbort off, as the config file declares.",
            Lower(rounding).Header[2]);
    }

    [Fact]
    public void Only_references_back_into_the_types_own_cycle_are_getters()
    {
        var module = Lower(Document(
            new()
            {
                ["Node"] = new([Field("leaf", new IrRef("Leaf")), Field("children", new IrArray(new IrRef("Node")))]),
                ["Leaf"] = new([Field("flag", Flag)]),
            },
            new IrResult(IrEncoding.Json, new IrRef("Node"), [])));

        var node = Assert.Single(module.Declarations, d => d.Name == "Node");
        Assert.Equal(
            new ObjectSchema([new ZodProperty("leaf", new Name("LeafSchema")), new ZodProperty("children", Z("array", new Name("NodeSchema")), Getter: true)], Strict: false),
            node.Schema);
    }

    [Fact]
    public void A_strict_setting_gives_strict_objects() =>
        Assert.Equal(
            new ObjectSchema([new ZodProperty("flag", Z("boolean"))], Strict: true),
            Lower(Document(new() { ["Row"] = new([Field("flag", Flag)]) }, new IrResult(IrEncoding.Rowset, new IrArray(new IrRef("Row")), [])), new ZodOptions { Objects = ZodObjects.Strict })
                .Declarations[0].Schema);

    [Fact]
    public void A_procedure_whose_results_cannot_be_listed_is_unknown_and_says_why() =>
        Assert.Equal(
            new ZodDeclaration("P", "s.p: its result sets cannot be listed; see the unknowns in the IR.", Z("unknown")),
            Procedure(Lower(Document([], null))));

    [Fact]
    public void Several_results_are_a_tuple_and_the_summary_describes_each()
    {
        var declaration = Procedure(Lower(Document([], new IrResult(IrEncoding.Json, Flag, [NoRows]), new IrResult(IrEncoding.Json, new IrArray(Flag), [NoRows]))));

        Assert.Equal("s.p: returns 2 result sets: JSON, null when no rows match; JSON, null when no rows match.", declaration.Summary);
        Assert.Equal(Z("tuple", new ArrayLiteral([Z("boolean").Then("nullable"), Bounded("min", 1, "Expected at least one row").Then("nullable")])), declaration.Schema);
    }

    [Fact]
    public void An_absent_json_array_reads_as_null_as_the_mssql_driver_hands_it_over() =>
        Assert.Equal(
            new ZodDeclaration("P", "s.p: returns JSON, null when no rows match.", Bounded("min", 1, "Expected at least one row").Then("nullable")),
            Procedure(Lower(Document([], new IrResult(IrEncoding.Json, new IrArray(Flag), [NoRows])))));

    [Theory]
    [InlineData(IrCardinality.One, false, "length", "Expected exactly one row", false)]
    [InlineData(IrCardinality.One, true, "length", "Expected exactly one row", true)]
    [InlineData(IrCardinality.AtMostOne, false, "length", "Expected exactly one row", false)]
    [InlineData(IrCardinality.AtMostOne, true, "length", "Expected exactly one row", true)]
    [InlineData(IrCardinality.Many, false, "min", "Expected at least one row", false)]
    [InlineData(IrCardinality.Many, true, "min", "Expected at least one row", true)]
    public void A_json_array_that_arrives_has_between_one_item_and_its_cardinality(IrCardinality cardinality, bool mayBeAbsent, string check, string expected, bool nullable)
    {
        // FOR JSON over no rows returns no row, which reads as null, so an array that arrives has an item for each of at least
        // one row.
        var result = new IrResult(IrEncoding.Json, new IrArray(Flag), mayBeAbsent ? [NoRows] : []) { Cardinality = cardinality };
        var bounded = Bounded(check, 1, expected);

        Assert.Equal(nullable ? bounded.Then("nullable") : bounded, Procedure(Lower(Document([], result))).Schema);
    }

    [Theory]
    [InlineData(0, null, "min", 1, "Expected at least one row")]
    [InlineData(2, null, "min", 2, "Expected at least 2 rows")]
    [InlineData(1, 1, "length", 1, "Expected exactly one row")]
    public void A_json_array_bounded_by_itself_and_by_its_cardinality_has_each_bound_written_once(int ownMin, int? ownMax, string check, int bound, string expected)
    {
        // A declared result's minItems, as the config's JSON Schema gives it.
        var result = new IrResult(IrEncoding.Json, new IrArray(Flag) { MinItems = ownMin, MaxItems = ownMax }, []);

        Assert.Equal(Bounded(check, bound, expected), Procedure(Lower(Document([], result))).Schema);
    }

    [Theory]
    [InlineData(IrCardinality.One, false, "length", "Expected exactly one row")]
    [InlineData(IrCardinality.One, true, "max", "Expected at most one row")]
    [InlineData(IrCardinality.AtMostOne, false, "max", "Expected at most one row")]
    public void A_result_of_rows_is_bounded_by_its_cardinality_in_rows(IrCardinality cardinality, bool mayBeAbsent, string check, string expected)
    {
        var result = new IrResult(IrEncoding.Rowset, new IrArray(Flag), mayBeAbsent ? [NoRows] : []) { Cardinality = cardinality };

        Assert.Equal(Bounded(check, 1, expected), Procedure(Lower(Document([], result))).Schema);
    }

    /// <summary>An array of booleans with one bound on its rows, and the bound's message.</summary>
    private static ZodExpression Bounded(string check, int count, string expected) =>
        Z("array", Z("boolean")).Then(check, new Number(count.ToString(CultureInfo.InvariantCulture)), new ObjectLiteral([new ZodProperty("error", new Text(expected))]));

    [Fact]
    public void A_result_of_many_rows_claims_no_count() =>
        Assert.Equal(Z("array", Z("boolean")), Procedure(Lower(Document([], new IrResult(IrEncoding.Rowset, new IrArray(Flag), [])))).Schema);

    [Fact]
    public void An_absent_rowset_reads_as_no_rows_rather_than_null() =>
        Assert.Equal(
            "s.p: returns rows, [] when it does not run.",
            Procedure(Lower(Document([], new IrResult(IrEncoding.Rowset, new IrArray(Flag), [new IrNullReason(IrNullRule.NotRun, "p.sql:3:1")])))).Summary);

    [Fact]
    public void A_result_the_config_declares_may_be_absent_names_the_declaration_as_its_reason() =>
        // The config file says the result may be absent, not why, so its summary names the declaration.
        Assert.Equal(
            "s.p: returns JSON, null when absent, as the config file declares.",
            Procedure(Lower(Document([], new IrResult(IrEncoding.Json, new IrArray(Flag), [new IrNullReason(IrNullRule.Declared, "s.p")])))).Summary);
}
