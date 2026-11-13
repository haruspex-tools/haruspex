using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The one rule a name written without a schema is resolved by (<see cref="SchemaNames.Resolve"/>): the one object of that
/// name in any schema, ignoring case; several, in a stable order, when which is read depends on the caller; none, when it
/// means <c>dbo</c>'s. Each user of it (FROM, a write's target, EXEC, a table-valued function, an alias type) has its own tests
/// of what it makes of each.
/// </summary>
public class SchemaNamesTests
{
    private static readonly DatabaseModel Shop = Model("""
        CREATE TABLE sales.customer (id int NOT NULL);
        GO
        CREATE TABLE dbo.other (id int NOT NULL);
        GO
        CREATE VIEW archive.Customer AS SELECT o.id FROM dbo.other AS o;
        GO
        CREATE TABLE dbo.customer (id int NOT NULL);
        """);

    private static DatabaseModel Model(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        return read.Model;
    }

    private static IEnumerable<SchemaObject> Readable(DatabaseModel model) => model.Tables.Cast<SchemaObject>().Concat(model.Readable);

    [Fact]
    public void A_name_one_schema_has_means_that_object()
    {
        var one = Assert.IsType<OneObject>(SchemaNames.Resolve(Readable(Shop), "OTHER"));

        Assert.Equal("dbo.other", one.Named.Name.ToString());
    }

    [Fact]
    public void A_name_several_schemas_have_means_each_in_the_order_of_their_names()
    {
        var several = Assert.IsType<SeveralObjects>(SchemaNames.Resolve(Readable(Shop), "CUSTOMER"));

        Assert.Equal(["archive.Customer view", "dbo.customer table", "sales.customer table"], several.Candidates.Select(o => $"{o.Name} {SchemaNames.Kind(o)}"));
    }

    [Fact]
    public void A_name_no_schema_has_means_dbo_s()
    {
        var none = Assert.IsType<NoObject>(SchemaNames.Resolve(Readable(Shop), "missing"));

        Assert.Equal(new ObjectName("dbo", "missing"), none.InDbo);
    }

    [Fact]
    public void Only_the_kinds_asked_for_count()
    {
        // The view archive.Customer is not a table, so among tables alone customer is still two, and other is one.
        Assert.Equal(2, Assert.IsType<SeveralObjects>(SchemaNames.Resolve(Shop.Tables, "customer")).Candidates.Count);
        Assert.IsType<NoObject>(SchemaNames.Resolve(Shop.Procedures, "customer"));
    }
}
