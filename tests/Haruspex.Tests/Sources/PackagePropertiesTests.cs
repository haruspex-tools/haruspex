using System.Xml.Linq;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A package's <c>SqlExtendedProperty</c> read as the levels a script's <c>sp_addextendedproperty</c> gives, from each element
/// form a build writes (observed with Microsoft.Build.Sql 2.3.0). A package names a table's, a view's and a table function's
/// column alike, and a procedure's and a function's parameter alike, so the owner is looked up among what the package defines,
/// and one it doesn't define is settled once the whole database is read: another package of it may define it
/// (docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md).
/// </summary>
public class PackagePropertiesTests
{
    private static readonly PackageObjects Defined = new(
        new HashSet<ObjectName> { new("shop", "orders") },
        new HashSet<ObjectName> { new("shop", "order_view") },
        new HashSet<ObjectName> { new("shop", "order_settings") },
        new HashSet<ObjectName> { new("shop", "order_settings"), new("shop", "settings_of") },
        new HashSet<ObjectName> { new("shop", "save_settings") });

    /// <summary>The element a package holds for the property <c>haruspex.jsonSchema</c>, named <paramref name="name"/> before the property's own name.</summary>
    private static XElement Element(string name) =>
        new(
            ModelXml.Name("Element"),
            new XAttribute("Type", "SqlExtendedProperty"),
            new XAttribute("Name", $"{name}.[haruspex.jsonSchema]"),
            new XElement(ModelXml.Name("Property"), new XAttribute("Name", "Value"), new XElement(ModelXml.Name("Value"), new XCData("N'a.json'"))));

    private static string Levels(string name) =>
        string.Join(" ", Assert.IsType<ExtendedProperty>(PackageProperties.Read(Element(name), "shop.dacpac", Defined)).Levels.Select(l => $"{l.Type}:{l.Name}"));

    [Theory]
    [InlineData("[SqlColumn].[shop].[orders].[settings]", "SCHEMA:shop TABLE:orders COLUMN:settings")]
    [InlineData("[SqlColumn].[shop].[order_view].[settings]", "SCHEMA:shop VIEW:order_view COLUMN:settings")]
    [InlineData("[SqlColumn].[shop].[order_settings].[settings]", "SCHEMA:shop FUNCTION:order_settings COLUMN:settings")]
    [InlineData("[SqlSubroutineParameter].[shop].[save_settings].[@settings]", "SCHEMA:shop PROCEDURE:save_settings PARAMETER:@settings")]
    [InlineData("[SqlSubroutineParameter].[shop].[settings_of].[@doc]", "SCHEMA:shop FUNCTION:settings_of PARAMETER:@doc")]
    [InlineData("[SqlIndexBase].[shop].[orders].[ix_orders_id]", "SCHEMA:shop TABLE:orders INDEX:ix_orders_id")]
    [InlineData("[SqlTableTypeColumn].[shop].[line_list].[doc]", "SCHEMA:shop TYPE:line_list COLUMN:doc")]
    [InlineData("[SqlType].[shop].[line_list]", "SCHEMA:shop TYPE:line_list")]
    [InlineData("[SqlFunction].[shop].[settings_of]", "SCHEMA:shop FUNCTION:settings_of")]
    [InlineData("[SqlTableBase].[shop].[orders]", "SCHEMA:shop TABLE:orders")]
    [InlineData("[SqlSchema].[shop]", "SCHEMA:shop")]
    public void Each_host_a_build_writes_is_read_as_the_levels_a_script_names(string name, string levels) =>
        Assert.Equal(levels, Levels(name));

    [Fact]
    public void A_host_with_no_level_keeps_its_element_type_so_that_a_reader_can_say_it_is_there() =>
        Assert.Equal("SCHEMA:shop SqlSomethingNew:orders SqlSomethingNew:part", Levels("[SqlSomethingNew].[shop].[orders].[part]"));

    [Theory]
    [InlineData("[SqlColumn].[shop].[base_orders].[settings]", "TABLE", "SCHEMA:shop TABLE:base_orders COLUMN:settings")]
    [InlineData("[SqlColumn].[shop].[base_view].[settings]", "VIEW", "SCHEMA:shop VIEW:base_view COLUMN:settings")]
    [InlineData("[SqlColumn].[shop].[nowhere].[settings]", null, "SCHEMA:shop TABLE:nowhere COLUMN:settings")]
    [InlineData("[SqlSubroutineParameter].[shop].[base_function].[@doc]", "FUNCTION", "SCHEMA:shop FUNCTION:base_function PARAMETER:@doc")]
    [InlineData("[SqlSubroutineParameter].[shop].[nowhere].[@doc]", null, "SCHEMA:shop PROCEDURE:nowhere PARAMETER:@doc")]
    public void An_owner_outside_the_package_is_settled_by_the_whole_database_and_one_nothing_defines_is_found_missing(string name, string? settled, string levels)
    {
        var read = Assert.IsType<ExtendedProperty>(PackageProperties.Read(Element(name), "app.dacpac", Defined));
        Assert.Equal(PackageProperties.UnsettledOwner, read.Levels[1].Type);

        var property = PackageProperties.Settle(read, _ => settled, _ => settled);

        Assert.Equal(levels, string.Join(" ", property.Levels.Select(l => $"{l.Type}:{l.Name}")));
    }
}
