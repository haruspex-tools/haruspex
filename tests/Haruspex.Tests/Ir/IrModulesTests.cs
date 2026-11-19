using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// A document split into modules: each object in the module it is given, and each named type in the one module whose objects
/// reach it, or else in the shared one.
/// </summary>
public class IrModulesTests
{
    private static readonly IrScalar Number = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrObjectType Type(params IrType[] fields) =>
        new([.. fields.Select((t, i) => new IrField("f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), t, IrPresence.Always, []))]);

    private static IrObject Procedure(string id, IrType result) =>
        new(id, id.Replace(".", "", StringComparison.Ordinal), IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Json, result, [])]);

    // sales.get_order and shop.get_product reach Total; only sales.get_order reaches Line; Orphan is reached by none.
    private static readonly IrDocument Shop = new(
        new IrSource("tsql"),
        [Procedure("sales.get_order", new IrRef("Order")), Procedure("shop.get_product", new IrRef("Product")), Procedure("sales.list_orders", Number)],
        new Dictionary<string, IrObjectType>
        {
            ["Order"] = Type(new IrRef("Total"), new IrArray(new IrRef("Line"))),
            ["Line"] = Type(Number),
            ["Product"] = Type(new IrRef("Total")),
            ["Total"] = Type(Number),
            ["Orphan"] = Type(Number),
        },
        []);

    private static readonly Dictionary<string, string> BySchema = new(StringComparer.Ordinal)
    {
        ["sales.get_order"] = "sales",
        ["shop.get_product"] = "shop",
        ["sales.list_orders"] = "sales",
    };

    [Fact]
    public void Each_object_is_in_the_module_it_is_given_and_the_modules_are_listed_in_ordinal_order()
    {
        var modules = IrModules.Of(Shop, BySchema);

        Assert.Equal(["sales", "shop"], modules.Names);
        Assert.Equal("shop", modules.Of(Shop.Objects[1]));
        Assert.Equal(["sales.get_order", "sales.list_orders"], modules.ObjectsOf("sales").Select(o => o.Id));
    }

    [Fact]
    public void A_type_reached_by_one_module_is_its_and_one_reached_by_two_or_none_is_shared()
    {
        var modules = IrModules.Of(Shop, BySchema);

        Assert.Equal("sales", modules.HomeOf("Order"));
        Assert.Equal("sales", modules.HomeOf("Line"));
        Assert.Equal("shop", modules.HomeOf("Product"));
        Assert.Equal(IrModules.Shared, modules.HomeOf("Total"));
        Assert.Equal(IrModules.Shared, modules.HomeOf("Orphan"));
        Assert.Equal(["sales", "shop"], modules.Reaching("Total").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Types_declared_once_for_several_live_where_all_of_them_are_reached()
    {
        var modules = IrModules.Of(Shop, BySchema);

        Assert.Equal("sales", modules.HomeOf(["Order", "Line"]));
        Assert.Equal(IrModules.Shared, modules.HomeOf(["Line", "Product"]));
    }

    [Fact]
    public void An_object_given_no_module_or_a_module_named_shared_is_refused()
    {
        Assert.Throws<ArgumentException>(() => IrModules.Of(Shop, new Dictionary<string, string>(BySchema.Where(m => m.Key != "shop.get_product"))));
        Assert.Throws<ArgumentException>(() => IrModules.Of(Shop, new Dictionary<string, string>(BySchema) { ["shop.get_product"] = IrModules.Shared }));
    }
}
