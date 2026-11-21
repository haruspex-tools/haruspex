using Haruspex.Collections;
using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// What the IR's walks over two documents make of a column's declared JSON (0.28): <c>check</c> sees a changed schema file, a
/// baseline's fit and combination treat any JSON as the type it is. Why:
/// docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md and docs/decisions/0094-any-json-is-a-type-of-its-own.md.
/// </summary>
public class IrDeclaredJsonTests
{
    private static readonly IrColumnDeclaration Settings = new(
        "shop.orders.settings",
        "schemas/settings.schema.json",
        null,
        new Dictionary<string, string> { ["schemas/settings.schema.json"] = new('a', 64) }.ToEquatableMap(f => f.Key, f => f.Value));

    /// <summary>A procedure returning one JSON object whose field <c>v</c> is of <paramref name="type"/>, with <paramref name="declared"/>.</summary>
    private static IrDocument Returning(IrType type, params IrColumnDeclaration[] declared) => new(
        new IrSource("tsql"),
        [new IrObject("shop.get_order", "GetOrder", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, new IrRef("Row"), [])])],
        new Dictionary<string, IrObjectType> { ["Row"] = new([new IrField("v", type, IrPresence.Always, [])]) },
        [])
    {
        Columns = [.. declared],
    };

    private static List<string> Changes(IrDocument before, IrDocument after) => [.. IrComparison.Compare(before, after).Select(c => c.ToString())];

    private static List<string> Fit(IrDocument application, IrDocument database) =>
        [.. IrFit.Of(application, database, new IrFitOptions()).Select(f => f.ToString())];

    [Fact]
    public void A_schema_file_whose_text_changed_is_a_change_though_no_type_did()
    {
        var before = Returning(new IrAnyJson(), Settings);
        var edited = Settings with { Files = new Dictionary<string, string> { ["schemas/settings.schema.json"] = new('b', 64) }.ToEquatableMap(f => f.Key, f => f.Value) };

        Assert.Equal(
            ["columns[\"shop.orders.settings\"]: changed from schemas/settings.schema.json (schemas/settings.schema.json aaaaaaaaaaaa) to schemas/settings.schema.json (schemas/settings.schema.json bbbbbbbbbbbb)"],
            Changes(before, Returning(new IrAnyJson(), edited)));
    }

    [Fact]
    public void A_declaration_added_removed_or_pointed_elsewhere_is_a_change()
    {
        var before = Returning(new IrAnyJson(), Settings);

        Assert.Equal(["columns[\"shop.orders.settings\"]: changed from schemas/settings.schema.json (schemas/settings.schema.json aaaaaaaaaaaa) to none"], Changes(before, Returning(new IrAnyJson()) with { Columns = [] }));
        Assert.Equal(["columns[\"shop.orders.settings\"]: changed from none to schemas/settings.schema.json (schemas/settings.schema.json aaaaaaaaaaaa)"], Changes(Returning(new IrAnyJson()), before));
        Assert.Single(Changes(before, Returning(new IrAnyJson(), Settings with { Pointer = "/$defs/other" })));
        Assert.Empty(Changes(before, Returning(new IrAnyJson(), Settings)));
    }

    [Fact]
    public void Any_json_narrowed_to_an_object_or_array_is_a_change_of_type()
    {
        Assert.Equal(
            ["shop.get_order results[0].v: type changed from any JSON to any JSON object or array"],
            Changes(Returning(new IrAnyJson()), Returning(new IrAnyJson(ObjectOrArray: true))));
    }

    [Fact]
    public void Any_json_accepts_whatever_the_database_gives_there_and_an_object_or_array_only_what_is_one()
    {
        var text = new IrScalar(IrScalarKind.String, null, null);

        Assert.Empty(Fit(Returning(new IrAnyJson()), Returning(text)));
        Assert.Empty(Fit(Returning(new IrAnyJson(ObjectOrArray: true)), Returning(new IrArray(text))));
        Assert.Empty(Fit(Returning(new IrAnyJson(ObjectOrArray: true)), Returning(new IrAnyJson(ObjectOrArray: true))));
        Assert.Equal(
            ["refused: shop.get_order results[0].v: type changed from any JSON object or array to string"],
            Fit(Returning(new IrAnyJson(ObjectOrArray: true)), Returning(text)));
        Assert.Equal(
            ["refused: shop.get_order results[0].v: type changed from any JSON object or array to any JSON"],
            Fit(Returning(new IrAnyJson(ObjectOrArray: true)), Returning(new IrAnyJson())));
    }

    [Fact]
    public void Any_json_combined_with_any_json_is_narrowed_only_where_both_are()
    {
        var combined = IrCombination.Of(Returning(new IrAnyJson(), Settings), Returning(new IrAnyJson(ObjectOrArray: true), Settings), new IrFitOptions()).Document;

        Assert.Equal(new IrAnyJson(), combined.Types["Row"].Fields[0].Type);
        Assert.Equal(Settings, Assert.Single(combined.Columns));
    }
}
