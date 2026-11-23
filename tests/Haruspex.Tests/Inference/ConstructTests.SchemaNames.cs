using Haruspex.Ir;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

// A name written without a schema in a module's definition is its schema's, then dbo's (SchemaSearch), for every kind of name;
// each rule observed in BindingObservationTests.ModuleSchema. The three tables of one name differ in their column's type, so a
// read of the wrong one shows.
public partial class ConstructTests
{
    private const string ThreeSchemas = """
        CREATE TABLE dbo.customer (name varchar(5) NOT NULL);
        GO
        CREATE TABLE sales.customer (name varchar(7) NOT NULL);
        GO
        CREATE TABLE archive.customer (name varchar(9) NOT NULL);
        GO
        CREATE TABLE archive.only_archive (name varchar(9) NOT NULL);
        GO
        CREATE TABLE dbo.no_sales (name varchar(5) NOT NULL);
        GO
        CREATE TABLE archive.no_sales (name varchar(9) NOT NULL);
        GO
        """;

    /// <summary>An alias type and a table type of one name in several schemas, of a length each.</summary>
    private const string AliasTypes = """
        CREATE TYPE dbo.code FROM varchar(5);
        GO
        CREATE TYPE sales.code FROM varchar(7);
        GO
        CREATE TYPE archive.code FROM varchar(9);
        GO
        CREATE TYPE dbo.lines AS TABLE (name varchar(5) NOT NULL);
        GO
        CREATE TYPE sales.lines AS TABLE (name varchar(7) NOT NULL);
        GO
        """;

    /// <summary>Infers the one procedure or view of <paramref name="module"/>, over <see cref="ThreeSchemas"/> and <paramref name="more"/>.</summary>
    private static IrDocument InSchemas(string module, string more = "") =>
        Infer(("tables.sql", ThreeSchemas + "\n" + more), ("p.sql", module));

    /// <summary>The length the field <paramref name="name"/> of the first result is checked against.</summary>
    private static int? Length(IrDocument doc, string name = "name") => ((IrScalar)Field(doc, name).Type).Facets?.Length?.Value;

    [Theory]
    [InlineData("CREATE PROCEDURE sales.p AS SELECT c.name FROM customer AS c;", 7)]
    [InlineData("CREATE PROCEDURE dbo.p AS SELECT c.name FROM customer AS c;", 5)]
    [InlineData("CREATE PROCEDURE sales.p AS SELECT c.name FROM no_sales AS c;", 5)]
    [InlineData("CREATE PROCEDURE archive.p AS SELECT c.name FROM no_sales AS c;", 9)]
    public void A_table_named_in_a_procedure_is_its_schemas_and_else_dbos(string procedure, int length)
    {
        var doc = InSchemas(procedure);

        Assert.Empty(doc.Unknowns);
        Assert.Equal(length, Length(doc));
    }

    [Fact]
    public void A_table_only_another_schema_has_is_unresolved_in_a_procedure()
    {
        // The procedure fails for every caller (208), so nothing is claimed, and the reason names where it was looked for.
        var doc = InSchemas("CREATE PROCEDURE sales.p AS SELECT c.name FROM only_archive AS c;");

        Assert.Equal(IrUnknownReason.UnresolvedTable, Unknown(doc, "results[0][].name").Reason);
    }

    [Theory]
    [InlineData("CREATE VIEW sales.v AS SELECT c.name FROM customer AS c;")]
    [InlineData("CREATE FUNCTION sales.v() RETURNS TABLE AS RETURN SELECT c.name FROM customer AS c;")]
    [InlineData("CREATE FUNCTION sales.v() RETURNS @r TABLE (name varchar(7) NOT NULL) AS BEGIN INSERT INTO @r SELECT c.name FROM customer AS c; RETURN; END;")]
    public void A_table_named_in_a_view_or_function_is_its_schemas_wherever_it_is_read(string module)
    {
        // Read from a dbo procedure, whose own rule would give dbo.customer's varchar(5).
        var read = module.Contains("FUNCTION", StringComparison.Ordinal) ? "sales.v()" : "sales.v";
        var doc = InferProcedure.Infer("dbo.p", ("tables.sql", ThreeSchemas), ("v.sql", module), ("p.sql", $"CREATE PROCEDURE dbo.p AS SELECT x.name FROM {read} AS x;"));

        Assert.Empty(doc.Unknowns);
        Assert.Equal(7, Length(doc));
    }

    [Theory]
    [InlineData("CREATE VIEW sales.customer_view AS SELECT c.name FROM sales.customer AS c;", "customer_view")]
    [InlineData("CREATE SYNONYM sales.customer_synonym FOR sales.customer;", "customer_synonym")]
    [InlineData("CREATE FUNCTION sales.customer_function() RETURNS TABLE AS RETURN SELECT c.name FROM sales.customer AS c;", "customer_function()")]
    public void A_view_synonym_or_function_named_in_a_procedure_is_its_schemas(string rival, string reference)
    {
        // dbo has one of each name too, reading dbo.customer, which the procedure in sales passes over.
        var dbo = rival.Replace("sales.", "dbo.", StringComparison.Ordinal);
        var doc = InSchemas($"CREATE PROCEDURE sales.p AS SELECT x.name FROM {reference} AS x;", $"{rival}\nGO\n{dbo}\nGO\n");

        Assert.Empty(doc.Unknowns);
        Assert.Equal(7, Length(doc));
    }

    [Fact]
    public void A_synonyms_target_without_a_schema_is_the_synonyms_schemas()
    {
        // Observed: A_synonyms_target_is_looked_for_in_the_synonyms_schema_then_dbo. Read from a dbo procedure, by full name.
        var doc = InSchemas("CREATE PROCEDURE dbo.p AS SELECT x.name FROM sales.near AS x;", "CREATE SYNONYM sales.near FOR customer;\nGO\n");

        Assert.Empty(doc.Unknowns);
        Assert.Equal(7, Length(doc));
    }

    [Fact]
    public void A_write_target_in_a_procedure_is_its_schemas()
    {
        // OUTPUT inserted.* gives the columns of the table written: sales.customer's varchar(7).
        var doc = InSchemas("CREATE PROCEDURE sales.p AS INSERT INTO customer (name) OUTPUT inserted.name VALUES ('a');");

        Assert.Empty(doc.Unknowns);
        Assert.Equal(7, Length(doc));
    }

    [Theory]
    [InlineData("CREATE PROCEDURE sales.p AS BEGIN DECLARE @v code = 'a'; SELECT @v AS name; END;")]
    [InlineData("CREATE PROCEDURE sales.p @v code AS SELECT @v AS name;")]
    [InlineData("CREATE PROCEDURE sales.p AS BEGIN DECLARE @t TABLE (name code NOT NULL); SELECT t.name FROM @t AS t; END;")]
    [InlineData("CREATE PROCEDURE sales.p @t lines READONLY AS SELECT t.name FROM @t AS t;")]
    public void An_alias_or_table_type_named_in_a_procedure_is_its_schemas(string procedure)
    {
        // Observed: An_alias_type_in_a_module_is_looked_for_in_the_modules_schema and the table type beside it.
        var doc = InSchemas(procedure, AliasTypes);

        Assert.DoesNotContain(doc.Unknowns, u => u.Path.Contains("name", StringComparison.Ordinal));
        Assert.Equal(7, Length(doc));
    }

    [Theory]
    [InlineData("CREATE FUNCTION sales.f() RETURNS code AS BEGIN RETURN 'abcdefghij'; END;", "sales.f() AS name")]
    [InlineData("CREATE FUNCTION sales.f() RETURNS @r TABLE (name code NOT NULL) AS BEGIN RETURN; END;", "r.name FROM sales.f() AS r")]
    [InlineData("CREATE FUNCTION sales.f(@v code) RETURNS TABLE AS RETURN SELECT @v AS name;", "r.name FROM sales.f('a') AS r")]
    [InlineData("CREATE VIEW sales.f AS SELECT j.name FROM OPENJSON(N'[{\"name\":\"a\"}]') WITH (name code) AS j;", "r.name FROM sales.f AS r")]
    // Observed: An_alias_type_in_a_module_is_looked_for_in_the_modules_schema, for a function's return type, its return table's
    // column and its parameter, and OPENJSON's WITH; each read from a dbo procedure, whose own rule would give dbo's varchar(5).
    public void An_alias_type_named_in_a_functions_or_views_definition_is_its_schemas(string module, string read)
    {
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("tables.sql", ThreeSchemas + "\n" + AliasTypes),
            ("f.sql", module),
            ("p.sql", $"CREATE PROCEDURE dbo.p AS SELECT {read};"));

        Assert.Equal(7, Length(doc));
    }

    [Fact]
    public void A_table_named_in_a_query_file_still_depends_on_the_callers_default_schema()
    {
        // A query file runs as its caller, whose default schema the project can't know: dbo, sales and archive all have customer.
        var doc = InferQuery.Infer(ThreeSchemas, "SELECT c.name FROM customer AS c;");

        Assert.Contains(doc.Unknowns, u => u.Reason == IrUnknownReason.UnresolvedTable && u.Detail.Contains("caller's default schema", StringComparison.Ordinal));
    }
}
