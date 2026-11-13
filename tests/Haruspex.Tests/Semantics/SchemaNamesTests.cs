using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The one rule a name written without a schema is resolved by (<see cref="SchemaNames"/>): every object of that name in any
/// schema, ignoring case, in a stable order, of the kinds the name may be. Each user of it (FROM, a write's target, EXEC,
/// a table-valued function, an alias type) has its own tests of what it makes of one, several or none.
/// </summary>
public class SchemaNamesTests
{
    private static DatabaseModel Model(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        return read.Model;
    }

    [Fact]
    public void An_unqualified_name_means_every_object_of_it_in_any_schema_in_the_order_of_their_names()
    {
        var model = Model("""
            CREATE TABLE sales.customer (id int NOT NULL);
            GO
            CREATE TABLE dbo.other (id int NOT NULL);
            GO
            CREATE VIEW archive.Customer AS SELECT o.id FROM dbo.other AS o;
            GO
            CREATE TABLE dbo.customer (id int NOT NULL);
            """);

        var found = SchemaNames.Unqualified(model.Tables.Cast<SchemaObject>().Concat(model.Readable), "CUSTOMER");

        Assert.Equal(["archive.Customer view", "dbo.customer table", "sales.customer table"], found.Select(o => $"{o.Name} {SchemaNames.Kind(o)}"));
    }

    [Fact]
    public void A_name_no_schema_has_means_nothing()
    {
        var model = Model("CREATE TABLE dbo.other (id int NOT NULL);");

        Assert.Empty(SchemaNames.Unqualified(model.Tables, "customer"));
    }
}
