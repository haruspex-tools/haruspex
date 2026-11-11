namespace Haruspex.Tests.Semantics;

public partial class SchemaFactsTests
{
    private const string Code = "CREATE TYPE dbo.code FROM nvarchar(10) NOT NULL;\nGO\n";

    [Fact]
    public void An_alias_type_resolves_with_its_facets_attributed_to_the_alias()
    {
        var facts = Facts(Code + "CREATE TABLE dbo.t (c dbo.code);");

        Assert.Equal("nvarchar(10)", facts.Type!.BuiltIn.ToString());
        Assert.Equal(10, facts.Length!.Value);
        Assert.Equal("type dbo.code", Source(facts.Length.From));
    }

    [Fact]
    public void An_unqualified_alias_type_resolves_when_one_schema_defines_it() =>
        Assert.Equal("nvarchar(10)", Facts(Code + "CREATE TABLE dbo.t (c code);").Type!.BuiltIn.ToString());

    [Fact]
    public void An_unqualified_alias_type_that_two_schemas_define_is_not_resolved() =>
        Assert.Null(Facts(Code + "CREATE TYPE other.code FROM int;\nGO\nCREATE TABLE dbo.t (c code);").Type);

    [Fact]
    public void An_unqualified_name_shared_with_a_clr_type_is_not_resolved() =>
        Assert.Null(Facts(Code + "CREATE TYPE other.code EXTERNAL NAME lib.[Code];\nGO\nCREATE TABLE dbo.t (c code);").Type);

    [Fact]
    public void An_alias_type_that_is_not_defined_is_not_resolved() =>
        Assert.Null(Facts("CREATE TABLE dbo.t (c dbo.nowhere);").Type);

    [Fact]
    public void Sysname_is_nvarchar_128()
    {
        var facts = Facts("CREATE TABLE dbo.t (c sysname);");

        Assert.Equal("nvarchar(128)", facts.Type!.BuiltIn.ToString());
        Assert.Equal("type sysname", Source(facts.Length!.From));
    }

    [Theory]
    [InlineData(Code + "CREATE TABLE dbo.t (c dbo.code);", true)]
    [InlineData(Code + "CREATE TABLE dbo.t (c dbo.code NULL);", false)]
    [InlineData("CREATE TYPE dbo.code FROM nvarchar(10) NULL;\nGO\nCREATE TABLE dbo.t (c dbo.code);", false)]
    [InlineData("CREATE TYPE dbo.code FROM nvarchar(10);\nGO\nCREATE TABLE dbo.t (c dbo.code);", false)]
    [InlineData("CREATE TABLE dbo.t (c sysname);", true)]
    [InlineData("CREATE TABLE dbo.t (c sysname NULL);", false)]
    public void A_column_that_declares_no_nullability_takes_its_alias_types(string sql, bool neverNull) =>
        Assert.Equal(neverNull, NeverNull(sql));
}
