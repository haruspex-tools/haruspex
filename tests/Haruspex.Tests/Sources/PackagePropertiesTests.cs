using System.Xml.Linq;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources.Dacpacs;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A package's <c>SqlExtendedProperty</c> read as the levels a script's <c>sp_addextendedproperty</c> gives. A package names a
/// table's, a view's and a table function's column alike, <c>[SqlColumn].[schema].[owner].[column].[name]</c> (observed with
/// Microsoft.Build.Sql 2.3.0), so the owner is looked up among what the package defines, and is never taken as a table's by default
/// (docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md).
/// </summary>
public class PackagePropertiesTests
{
    private static readonly HashSet<ObjectName> Tables = [new("shop", "orders")];
    private static readonly HashSet<ObjectName> Views = [new("shop", "order_view")];
    private static readonly HashSet<ObjectName> TableFunctions = [new("shop", "order_settings")];
    private static readonly HashSet<ObjectName> Procedures = [new("shop", "save_settings")];

    /// <summary>The element a package holds for the property <c>haruspex.jsonSchema</c> on <paramref name="host"/>, as observed.</summary>
    private static XElement Element(string host, string owner, string part) =>
        new(
            ModelXml.Name("Element"),
            new XAttribute("Type", "SqlExtendedProperty"),
            new XAttribute("Name", $"[{host}].[shop].[{owner}].[{part}].[haruspex.jsonSchema]"),
            new XElement(ModelXml.Name("Property"), new XAttribute("Name", "Value"), new XElement(ModelXml.Name("Value"), new XCData("N'a.json'"))));

    [Theory]
    [InlineData("SqlColumn", "orders", "settings", "TABLE", "COLUMN")]
    [InlineData("SqlColumn", "order_view", "settings", "VIEW", "COLUMN")]
    [InlineData("SqlColumn", "order_settings", "settings", "FUNCTION", "COLUMN")]
    [InlineData("SqlSubroutineParameter", "save_settings", "@settings", "PROCEDURE", "PARAMETER")]
    [InlineData("SqlSubroutineParameter", "settings_of", "@doc", "FUNCTION", "PARAMETER")]
    public void A_column_s_or_parameter_s_owner_is_the_kind_of_object_the_package_defines(string host, string owner, string part, string ownerLevel, string partLevel)
    {
        var property = PackageProperties.Read(Element(host, owner, part), "shop.dacpac", Tables, Views, TableFunctions, Procedures);

        Assert.NotNull(property);
        Assert.Equal(
            [("SCHEMA", "shop"), (ownerLevel, owner), (partLevel, part)],
            property.Levels.Select(l => (l.Type, l.Name)));
        Assert.Equal(("haruspex.jsonSchema", "a.json"), (property.Name, property.Value));
    }

    [Fact]
    public void A_column_whose_owner_is_no_table_view_or_table_function_of_the_package_is_passed_over() =>
        Assert.Null(PackageProperties.Read(Element("SqlColumn", "elsewhere", "settings"), "shop.dacpac", Tables, Views, TableFunctions, Procedures));
}
