using Haruspex.Diagnostics;
using Haruspex.Emit.Zod;
using Haruspex.Emit.Zod.Model;
using Haruspex.Emit.Zod.Planning;
using Haruspex.Ir;

namespace Haruspex.Tests.Emit.Zod;

/// <summary>The plan: what a module declares, in what order, what it imports, and what it refuses before building anything.</summary>
public class ZodPlannerTests
{
    private static readonly IrScalar Number = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrObjectType Type(params IrType[] fields) =>
        new([.. fields.Select((t, i) => new IrField("f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), t, IrPresence.Always, []))]);

    private static IrObject Procedure(string id, params IrResult[] results) =>
        new(id, id.Split('.')[1].ToUpperInvariant(), IrObjectKind.Procedure, "p.sql:1:1", [], [.. results]);

    private static IrDocument Document(Dictionary<string, IrObjectType> types, params IrObject[] objects) => new(new IrSource("tsql"), [.. objects], types, []);

    private static IrResult Json(IrType type) => new(IrEncoding.Json, type, []);

    private static IrResult Rows(string row) => new(IrEncoding.Rowset, new IrArray(new IrRef(row)), []);

    private static ZodPlan Plan(IrDocument document) => ZodPlanner.Plan(document, new ZodOptions()).Expected();

    /// <summary>What the plan refused, written as the CLI writes it.</summary>
    private static string Refused(IrDocument document) =>
        Assert.Single(Assert.IsType<Failed<ZodPlan>>(ZodPlanner.Plan(document, new ZodOptions())).Diagnostics).ToString();

    private static List<string> Order(ZodPlan plan) =>
        [.. plan.Entries.Select(e => e switch
        {
            ZodPlanEntry.NamedType named => named.Name,
            ZodPlanEntry.TableRow row => row.Name,
            ZodPlanEntry.Procedure procedure => procedure.Object.Id,
        })];

    [Fact]
    public void Declares_each_type_after_those_it_uses_and_before_its_first_procedure()
    {
        var plan = Plan(Document(
            new() { ["Order"] = Type(new IrRef("Customer")), ["Customer"] = Type(Number), ["Note"] = Type(Number) },
            Procedure("s.b_notes", Rows("Note")),
            Procedure("s.a_orders", Json(new IrRef("Order")))));

        Assert.Equal(["Customer", "Order", "s.a_orders", "Note", "s.b_notes"], Order(plan));
    }

    [Fact]
    public void A_type_two_procedures_share_is_declared_once()
    {
        var plan = Plan(Document(
            new() { ["Shared"] = Type(Number) },
            Procedure("s.one", Rows("Shared")),
            Procedure("s.two", Rows("Shared"))));

        Assert.Equal(["Shared", "s.one", "s.two"], Order(plan));
    }

    [Fact]
    public void A_type_no_result_reaches_is_not_declared()
    {
        var plan = Plan(Document(new() { ["Row"] = Type(Number), ["Orphan"] = Type(Number) }, Procedure("s.p", Rows("Row"))));

        Assert.Equal(["Row", "s.p"], Order(plan));
    }

    [Fact]
    public void A_type_inside_json_text_in_a_rowset_is_planned_as_json()
    {
        var plan = Plan(Document(
            new() { ["Row"] = Type(new IrJsonText(new IrRef("Payload"))), ["Payload"] = Type(Number) },
            Procedure("s.p", Rows("Row"))));

        Assert.Equal(
            [new ZodPlanEntry.NamedType("Payload", plan.Document.Types["Payload"], IrEncoding.Json), new ZodPlanEntry.NamedType("Row", plan.Document.Types["Row"], IrEncoding.Rowset)],
            plan.Entries.OfType<ZodPlanEntry.NamedType>());
        Assert.True(plan.UsesJsonText);
    }

    [Fact]
    public void The_json_text_helper_is_planned_only_for_json_text_of_known_content() =>
        Assert.False(Plan(Document(new() { ["Row"] = Type(new IrJsonText()) }, Procedure("s.p", Rows("Row")))).UsesJsonText);

    [Fact]
    public void Imports_are_grouped_by_module_with_modules_and_names_sorted()
    {
        var plan = Plan(Document(
            new() { ["Row"] = Type(new IrImport("Zeta", "./b"), new IrImport("Alpha", "./b"), new IrImport("Mid", "@app/a"), new IrImport("Alpha", "./b")) },
            Procedure("s.p", Rows("Row"))));

        Assert.Equal([new ZodImport("./b", ["Alpha", "Zeta"]), new ZodImport("@app/a", ["Mid"])], plan.Imports);
    }

    [Fact]
    public void A_name_imported_from_two_modules_is_refused()
    {
        var document = Document(new() { ["Row"] = Type(new IrImport("S", "./a"), new IrImport("S", "./b")) }, Procedure("s.p", Rows("Row")));

        Assert.Equal("haruspex: error HAR5003: 'S' is imported from both './a' and './b'.", Refused(document));
    }

    [Theory]
    [InlineData("RowSchema")]
    [InlineData("z")]
    [InlineData("jsonText")]
    public void An_imported_name_the_module_binds_is_refused(string name)
    {
        var document = Document(new() { ["Row"] = Type(new IrImport(name, "./a")) }, Procedure("s.p", Rows("Row")));

        Assert.Equal($"haruspex: error HAR5004: The imported name '{name}' is also a name this module defines or uses.", Refused(document));
    }

    [Fact]
    public void Every_refusal_is_reported_not_only_the_first()
    {
        var document = Document(new() { ["string"] = Type(new IrImport("S", "./a"), new IrImport("S", "./b")) }, Procedure("s.p", Rows("string")));

        Assert.Equal(
            [
                "haruspex: error HAR5003: 'S' is imported from both './a' and './b'.",
                "haruspex: error HAR5001: The IR name 'string' is not a valid TypeScript identifier.",
            ],
            Assert.IsType<Failed<ZodPlan>>(ZodPlanner.Plan(document, new ZodOptions())).Diagnostics.Select(d => d.ToString()));
    }

    [Fact]
    public void A_type_name_that_typescript_reserves_is_refused()
    {
        var document = Document(new() { ["string"] = Type(Number) }, Procedure("s.p", Rows("string")));

        Assert.Equal("haruspex: error HAR5001: The IR name 'string' is not a valid TypeScript identifier.", Refused(document));
    }

    [Fact]
    public void A_type_one_result_reads_as_rows_and_another_as_json_is_refused()
    {
        var document = Document(new() { ["Shared"] = Type(Number) }, Procedure("s.rows", Rows("Shared")), Procedure("s.json", Json(new IrRef("Shared"))));

        // haruspex's front end never makes one, but an IR is a file anything can write.
        Assert.Equal("haruspex: error HAR5005: The type Shared is read both as rows and as JSON, so one schema can't check it.", Refused(document));
    }

    [Fact]
    public void A_procedure_whose_results_cannot_be_listed_is_planned_alone()
    {
        var unlisted = new IrObject("s.p", "P", IrObjectKind.Procedure, "p.sql:1:1", [], null);

        Assert.Equal(["s.p"], Order(Plan(Document([], unlisted))));
    }
}
