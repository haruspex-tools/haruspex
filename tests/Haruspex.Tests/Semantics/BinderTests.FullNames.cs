using Haruspex.SqlServer.Semantics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.Tests.Semantics;

// A column or star qualified by its table's schema and name (dbo.t.id, dbo.t.*), each rule observed in BindingObservationTests.
public partial class BinderTests
{
    private const string Shop = """
        CREATE SCHEMA shop;
        GO
        CREATE TABLE shop.orders (order_id int NOT NULL PRIMARY KEY, status varchar(10) NOT NULL);
        GO
        """;

    [Fact]
    public void A_column_of_three_parts_is_the_column_of_the_source_exposed_under_that_schema_and_name()
    {
        // Observed: A_column_of_three_parts_binds_to_the_table_named_so_in_from.
        var column = Assert.IsType<TableColumn>(new Sample("SELECT dbo.t.name FROM dbo.t JOIN dbo.u AS u ON u.t_id = dbo.t.id;").Resolve("dbo.t.name"));

        Assert.Equal("dbo.t", column.Table.Name.ToString());
        Assert.Equal("name", column.Column.Name);
    }

    [Fact]
    public void A_column_of_two_parts_still_binds_by_the_table_s_name_alone() =>
        Assert.Equal("dbo.t", Assert.IsType<TableColumn>(new Sample("SELECT t.name FROM dbo.t;").Resolve("t.name")).Table.Name.ToString());

    [Fact]
    public void A_full_name_is_compared_ignoring_case() =>
        Assert.IsType<TableColumn>(new Sample("SELECT DBO.T.NAME FROM dbo.t;").Resolve("DBO.T.NAME"));

    [Fact]
    public void A_source_with_an_alias_is_not_known_by_its_full_name()
    {
        // Observed: An_alias_hides_a_tables_full_name (4104).
        var resolution = Assert.IsType<MissingColumn>(new Sample("SELECT dbo.t.name FROM dbo.t AS x;").Resolve("dbo.t.name"));

        Assert.Equal(BindingProblemKind.UnresolvedColumn, resolution.Problem.Kind);
        Assert.Equal("No source in scope is named dbo.t: the table of that name has an alias, which hides its own name.", resolution.Problem.Detail);
    }

    [Theory]
    [InlineData("WITH c AS (SELECT t.id FROM dbo.t AS t) SELECT dbo.c.id FROM c;", "dbo.c.id", "dbo.c")]
    [InlineData("SELECT dbo.[@v].id FROM dbo.t;", "dbo.[@v].id", "dbo.@v")]
    public void A_full_name_no_source_has_says_so_without_naming_an_alias(string statement, string reference, string name) =>
        Assert.Equal(
            $"No source in scope is named {name}, as a column of three parts needs.",
            Assert.IsType<MissingColumn>(new Sample(statement).Resolve(reference)).Problem.Detail);

    [Fact]
    public void A_full_name_an_inner_query_hides_with_an_alias_is_the_enclosing_query_s()
    {
        // Observed: A_full_name_reaches_past_an_aliased_source_to_the_enclosing_query.
        var body = new Sample("SELECT (SELECT TOP (1) dbo.t.code FROM dbo.t AS i WHERE i.id = 2) AS c FROM dbo.t;");
        var column = Assert.IsType<TableColumn>(body.Resolve("dbo.t.code"));

        Assert.Null(((NamedTableReference)column.Source.Syntax).Alias);
    }

    [Fact]
    public void A_full_name_an_inner_query_s_table_of_that_name_in_another_schema_lacks_is_the_enclosing_query_s()
    {
        // Observed: A_full_name_passes_an_inner_table_of_its_name_in_another_schema. The inner dbo.orders has no alias, and is
        // exposed as orders, but not as shop.orders: the full name is the outer table's.
        var body = new Sample("SELECT (SELECT TOP (1) shop.orders.status FROM dbo.orders) AS s FROM shop.orders;", Shop + "\nCREATE TABLE dbo.orders (order_id int NOT NULL, status varchar(10) NULL);\nGO\n");
        var column = Assert.IsType<TableColumn>(body.Resolve("shop.orders.status"));

        Assert.Equal("shop.orders", column.Table.Name.ToString());
        Assert.Same(((BoundSpecification)body.Query()).Scope.Sources[0], column.Source);
    }

    [Fact]
    public void A_full_name_an_inner_query_exposes_too_is_the_inner_query_s()
    {
        // Observed: A_full_name_both_queries_expose_is_the_inner_ones.
        var body = new Sample("SELECT (SELECT TOP (1) shop.orders.status FROM shop.orders) AS s FROM shop.orders;", Shop);

        Assert.NotSame(((BoundSpecification)body.Query()).Scope.Sources[0], Assert.IsType<TableColumn>(body.Resolve("shop.orders.status")).Source);
    }

    [Theory]
    [InlineData("WITH t AS (SELECT 7 AS code) SELECT (SELECT MAX(dbo.t.code) FROM t) AS m FROM dbo.t;")]
    [InlineData("SELECT (SELECT MAX(dbo.t.code) FROM (SELECT 7 AS code) AS t) AS m FROM dbo.t;")]
    public void A_full_name_an_inner_cte_or_derived_table_of_that_name_lacks_is_the_enclosing_query_s(string statement)
    {
        // Observed: A_full_name_passes_an_inner_cte_or_derived_table_of_its_name: neither has a full name.
        var body = new Sample(statement);

        Assert.Same(((BoundSpecification)body.Query()).Scope.Sources[0], Assert.IsType<TableColumn>(body.Resolve("dbo.t.code")).Source);
    }

    [Theory]
    [InlineData("SELECT dbo.t.name FROM t;", true)]
    [InlineData("SELECT shop.orders.status FROM orders;", true)]
    [InlineData("SELECT dbo.orders.status FROM orders;", false)]
    public void A_one_part_name_in_from_is_exposed_under_the_schema_it_resolves_to(string statement, bool binds)
    {
        // Observed: A_one_part_name_is_exposed_under_the_schema_it_resolves_to. Only shop has orders, so a caller it runs for at
        // all reads shop.orders, under whatever default schema.
        var body = new Sample(statement, Shop);
        var reference = statement["SELECT ".Length..statement.IndexOf(" FROM", StringComparison.Ordinal)];

        Assert.Equal(binds, body.Resolve(reference) is TableColumn);
    }

    [Fact]
    public void A_one_part_name_whose_schema_depends_on_the_caller_leaves_a_full_name_unbound()
    {
        // dbo.t and other.t: which one t reads, and so which full name it is exposed under, is the caller's default schema's.
        var body = new Sample("SELECT dbo.t.code FROM t;", "CREATE SCHEMA other;\nGO\nCREATE TABLE other.t (id int NOT NULL, code int NOT NULL);\nGO\n");

        Assert.Equal(BindingProblemKind.UnresolvedTable, Assert.IsType<UnboundSource>(body.Resolve("dbo.t.code")).Problem.Kind);
    }

    [Fact]
    public void Two_sources_exposed_under_one_name_are_ambiguous_as_sql_server_refuses_them()
    {
        // Observed: Two_sources_of_one_name_without_aliases_are_refused (1013): SQL Server refuses even shop.orders beside
        // sales.orders, so the name can't mean either.
        var body = new Sample("SELECT shop.orders.status FROM shop.orders CROSS JOIN shop.orders;", Shop);
        var resolution = Assert.IsType<AmbiguousColumn>(body.Resolve("shop.orders.status"));

        Assert.Contains("1013", resolution.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_containing_dots_are_bound_by_their_bracketed_parts()
    {
        // Observed: Bracketed_names_containing_dots_bind_as_three_parts.
        var body = new Sample(
            "SELECT [sales.eu].[order.line].qty FROM [sales.eu].[order.line];",
            "CREATE SCHEMA [sales.eu];\nGO\nCREATE TABLE [sales.eu].[order.line] (line_id int NOT NULL, qty int NOT NULL);\nGO\n");

        Assert.Equal("qty", Assert.IsType<TableColumn>(body.Resolve("[sales.eu].[order.line].qty")).Column.Name);
    }

    [Fact]
    public void A_view_a_synonym_and_a_function_are_exposed_under_their_own_names()
    {
        // Observed: A_view_a_synonym_and_a_function_bind_by_their_own_full_names. A synonym is known by its name, not its target's.
        const string More = """
            CREATE VIEW dbo.v AS SELECT t.id FROM dbo.t AS t;
            GO
            CREATE SYNONYM dbo.s FOR dbo.t;
            GO
            CREATE FUNCTION dbo.f() RETURNS TABLE AS RETURN SELECT t.id FROM dbo.t AS t;
            GO
            """;

        Assert.IsType<ViewRelation>(Assert.IsType<RelationColumn>(new Sample("SELECT dbo.v.id FROM dbo.v;", More).Resolve("dbo.v.id")).Relation);
        Assert.IsType<TableColumn>(new Sample("SELECT dbo.s.id FROM dbo.s;", More).Resolve("dbo.s.id"));
        Assert.IsType<MissingColumn>(new Sample("SELECT dbo.t.id FROM dbo.s;", More).Resolve("dbo.t.id"));
        Assert.IsType<TableFunctionCall>(Assert.IsType<RelationColumn>(new Sample("SELECT dbo.f.id FROM dbo.f();", More).Resolve("dbo.f.id")).Relation);
    }

    [Theory]
    [InlineData("SELECT dbo.#x.a FROM #x;", "dbo.#x.a")]
    [InlineData("SELECT shop.#x.a FROM #x;", "shop.#x.a")]
    public void A_temporary_table_is_known_by_its_name_under_any_schema(string statement, string reference)
    {
        // Observed: A_temporary_tables_full_name_takes_any_schema.
        var known = NameEnvironment.Empty.WithTemporaryColumns([KeyValuePair.Create(new LocalName("#x"), (IReadOnlyList<string>)["a"])]);

        Assert.IsType<TemporaryTable>(Assert.IsType<RelationColumn>(new Sample(statement).Resolve(reference, known)).Relation);
    }

    [Fact]
    public void A_cte_has_no_full_name() =>
        // Observed: A_cte_has_no_full_name (4104).
        Assert.IsType<MissingColumn>(new Sample("WITH c AS (SELECT t.id FROM dbo.t AS t) SELECT dbo.c.id FROM c;").Resolve("dbo.c.id"));

    [Fact]
    public void A_full_name_over_a_source_of_another_database_the_project_does_not_hold_is_unbound()
    {
        // elsewhere may be the database the procedure runs in, where SQL Server binds the full name (observed:
        // A_full_name_binds_a_source_named_with_this_databases_name), or another, where it doesn't (4104); which is not known.
        var resolution = Assert.IsType<UnboundSource>(new Sample("SELECT dbo.t.id FROM elsewhere.dbo.t;").Resolve("dbo.t.id"));

        Assert.Equal(BindingProblemKind.UnsupportedSource, resolution.Problem.Kind);
    }

    [Fact]
    public void A_column_reference_of_four_parts_is_not_modelled()
    {
        var resolution = Assert.IsType<UnmodelledReference>(new Sample("SELECT here.dbo.t.id FROM dbo.t;").Resolve("here.dbo.t.id"));

        Assert.Equal(BindingProblemKind.UnsupportedReference, resolution.Problem.Kind);
        Assert.Equal("The column reference here.dbo.t.id names its database, which is not modelled yet.", resolution.Problem.Detail);
    }

    // ---- a star --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_star_qualified_by_schema_and_name_is_its_source_s_columns() =>
        Assert.Equal(["id", "name", "code"], new Sample("SELECT dbo.t.* FROM dbo.t JOIN dbo.u AS u ON u.t_id = dbo.t.id;").Query().Names);

    [Theory]
    [InlineData("SELECT dbo.t.* FROM dbo.t AS x;", BindingProblemKind.UnresolvedColumn)]
    [InlineData("SELECT here.dbo.t.* FROM dbo.t;", BindingProblemKind.UnsupportedStar)]
    public void A_star_of_a_full_name_no_source_is_exposed_under_is_not_expanded(string statement, BindingProblemKind kind)
    {
        // Observed: An_alias_hides_a_tables_full_name (Msg 107 for the star).
        var body = new Sample(statement);
        var query = (BoundSpecification)body.Query();
        var star = (SelectStarExpression)query.Query.SelectElements[0];

        Assert.Equal(kind, Assert.IsType<UnexpandedStar>(body.Binder.Star(star, query.Scope)).Problem.Kind);
    }
}
