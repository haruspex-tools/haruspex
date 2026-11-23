using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

/// <summary>
/// The rule a name written without a schema is resolved by (<see cref="SchemaNames.Resolve"/>), by where it is written. By the
/// caller's rule: the one object of that name in any schema, ignoring case; several, in a stable order, when which is read
/// depends on the caller; none, when it means <c>dbo</c>'s. In a module's definition: its own schema's object, or else
/// <c>dbo</c>'s, and never another schema's. Each user of it (FROM, a write's target, EXEC, a table-valued function, an alias
/// type) has its own tests of what it makes of each.
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
        GO
        CREATE VIEW archive.archived AS SELECT o.id FROM dbo.other AS o;
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
        var one = Assert.IsType<OneObject>(SchemaNames.Resolve(Readable(Shop), "OTHER", SchemaSearch.Caller));

        Assert.Equal("dbo.other", one.Named.Name.ToString());
    }

    [Fact]
    public void A_name_several_schemas_have_means_each_in_the_order_of_their_names()
    {
        var several = Assert.IsType<SeveralObjects>(SchemaNames.Resolve(Readable(Shop), "CUSTOMER", SchemaSearch.Caller));

        Assert.Equal(["archive.Customer view", "dbo.customer table", "sales.customer table"], several.Candidates.Select(o => $"{o.Name} {SchemaNames.Kind(o)}"));
    }

    [Fact]
    public void A_name_no_schema_has_means_dbo_s()
    {
        var none = Assert.IsType<NoObject>(SchemaNames.Resolve(Readable(Shop), "missing", SchemaSearch.Caller));

        Assert.Equal(new ObjectName("dbo", "missing"), none.InDbo);
    }

    [Fact]
    public void Only_the_kinds_asked_for_count()
    {
        // The view archive.Customer is not a table, so among tables alone customer is still two, and other is one.
        Assert.Equal(2, Assert.IsType<SeveralObjects>(SchemaNames.Resolve(Shop.Tables, "customer", SchemaSearch.Caller)).Candidates.Count);
        Assert.IsType<NoObject>(SchemaNames.Resolve(Shop.Procedures, "customer", SchemaSearch.Caller));
    }

    [Theory]
    [InlineData("sales", "customer", "sales.customer")]
    [InlineData("SALES", "Customer", "sales.customer")]
    [InlineData("archive", "customer", "archive.Customer")]
    [InlineData("dbo", "customer", "dbo.customer")]
    [InlineData("sales", "other", "dbo.other")]
    // Observed: A_name_in_a_procedure_is_looked_for_in_the_procedures_schema_whoever_calls, and
    // A_name_the_procedures_schema_lacks_is_dbos_and_never_another_schemas.
    public void A_name_in_a_module_means_its_own_schemas_object_and_else_dbos(string schema, string name, string means) =>
        Assert.Equal(means, Assert.IsType<OneObject>(SchemaNames.Resolve(Readable(Shop), name, SchemaSearch.Within(schema))).Named.Name.ToString());

    [Fact]
    public void A_name_in_a_module_never_means_another_schemas_object()
    {
        // Only archive has archived: by the caller's rule a caller who runs it at all reads that, but a module in sales fails.
        Assert.Equal("archive.archived", Assert.IsType<OneObject>(SchemaNames.Resolve(Readable(Shop), "archived", SchemaSearch.Caller)).Named.Name.ToString());
        Assert.Equal(new ObjectName("dbo", "archived"), Assert.IsType<NoObject>(SchemaNames.Resolve(Readable(Shop), "archived", SchemaSearch.Within("sales"))).InDbo);
    }
}
