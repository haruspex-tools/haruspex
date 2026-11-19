using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Table-valued parameters and table variables of a table type: the parameter is an array of the type's rows, naming the type's
/// definition, and a query that reads either is typed as a query over a table of the type's columns, keys and CHECK constraints
/// (<c>ParameterObservationTests</c> observes what the server does with a row sent).
/// </summary>
public partial class ParameterTests
{
    private const string LineList = """
        CREATE TYPE shop.line_list AS TABLE (
            product_id int NOT NULL,
            qty smallint NOT NULL CHECK (qty > 0),
            note nvarchar(200) NULL DEFAULT N'none',
            PRIMARY KEY (product_id)
        );
        """;

    /// <summary>The document of <c>shop.p</c>, defined by <paramref name="procedure"/>, beside <paramref name="types"/>.</summary>
    private static IrDocument WithTypes(string procedure, params string[] types)
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", "CREATE SCHEMA shop;");
        for (var i = 0; i < types.Length; i++)
            dir.File($"t{i}.sql", types[i]);
        dir.File("p.sql", procedure);
        var project = SqlProject.Load(dir.Path);
        return project.Infer(project.Select([ObjectPattern.Parse("shop.p")]).Procedures, HaruspexConfig.None(".")).Expected();
    }

    private static IrObjectType RowOf(IrDocument document, IrParameter parameter) =>
        document.Types[Assert.IsType<IrRef>(Assert.IsType<IrArray>(parameter.Type).Items).Name];

    [Fact]
    public void A_table_valued_parameter_is_an_array_of_its_type_s_rows_naming_the_definition_it_carries()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @lines shop.line_list READONLY AS SELECT 1 AS one;", LineList);
        var parameter = Assert.Single(document.Objects[0].Parameters);
        var row = RowOf(document, parameter);
        var definition = document.TableTypes["shop.line_list"];

        Assert.Equal(("shop.line_list", true, false), (parameter.TableType, parameter.HasDefault, parameter.IsOutput));
        Assert.Equal(new IrTypeIdentity(IrTypeOwner.TableType, "shop.line_list", "<shop.line_list>"), row.Identity);

        // Every column, in the type's order, each key always there; NULL only where the column takes it.
        Assert.Equal(["product_id", "qty", "note"], row.Fields.Select(f => f.Name));
        Assert.All(row.Fields, f => Assert.Equal(IrPresence.Always, f.Presence));
        Assert.Equal([false, false, true], row.Fields.Select(f => f.Nullable));

        // The CHECK is a fact of the column, which the server holds every row sent to (547).
        Assert.Equal(("0", false), (Assert.IsType<IrScalar>(row.Fields[1].Type).Facets?.Min?.Value, Assert.IsType<IrScalar>(row.Fields[1].Type).Facets!.Min!.Inclusive));
        Assert.Equal(("shop", "line_list"), (definition.Schema, definition.Name));
        Assert.Equal(["int", "smallint", "nvarchar(200)"], definition.Columns.Select(c => c.Sql));
        Assert.Equal(["product_id"], Assert.Single(definition.Keys).Columns);
        Assert.Equal(["qty > 0"], definition.Checks);
        Assert.Equal(IrDocument.TableTypesVersion, document.Version);
    }

    [Theory]
    [InlineData("an identity column", "serial int IDENTITY", "an identity column, serial, so no row can be sent in it (error 1077, even for none)")]
    [InlineData("a computed column", "serial AS label + N'!'", "a computed column, serial, so no row can be sent in it (error 271, even for none)")]
    [InlineData("a rowversion column", "serial rowversion", "a rowversion column, serial, so no row can be sent in it (error 273, even for none)")]
    public void A_table_type_with_a_column_the_server_fills_is_a_parameter_that_can_only_be_left_out(string kind, string column, string why)
    {
        var document = WithTypes(
            "CREATE PROCEDURE shop.p @serials shop.serial_list READONLY AS SELECT count(*) AS n FROM @serials;",
            $"CREATE TYPE shop.serial_list AS TABLE (label nvarchar(20) NOT NULL, {column});");
        var parameter = Assert.Single(document.Objects[0].Parameters);

        Assert.True(parameter.Type is IrUnknownType { Reason: IrUnknownReason.UnsupportedSqlType }, kind);
        Assert.Equal("shop.serial_list", parameter.TableType);
        Assert.Equal($"The table type shop.serial_list has {why}; the parameter can only be left out.", Assert.Single(document.Unknowns, u => u.Path == "parameters.@serials").Detail);
    }

    [Fact]
    public void A_table_type_with_a_column_of_a_type_not_modelled_is_an_unknown_parameter()
    {
        var document = WithTypes(
            "CREATE PROCEDURE shop.p @blobs shop.blob_list READONLY AS SELECT 1 AS one;",
            "CREATE TYPE shop.blob_list AS TABLE (id int NOT NULL, data varbinary(4) NULL);");

        Assert.IsType<IrUnknownType>(Assert.Single(document.Objects[0].Parameters).Type);
        Assert.Equal(
            "The table type shop.blob_list has its column data, of the type varbinary(4), which is not modelled yet, so a row of it isn't typed.",
            Assert.Single(document.Unknowns, u => u.Path == "parameters.@blobs").Detail);
    }

    [Fact]
    public void A_table_valued_parameter_of_a_type_the_project_lacks_is_unknown()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @lines shop.missing READONLY AS SELECT 1 AS one;");
        var parameter = Assert.Single(document.Objects[0].Parameters);

        Assert.IsType<IrUnknownType>(parameter.Type);
        Assert.Null(parameter.TableType);
        Assert.Empty(document.TableTypes);
    }

    [Fact]
    public void A_query_over_a_table_valued_parameter_reads_each_column_as_the_type_declares_it()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @lines shop.line_list READONLY AS SELECT l.product_id, l.qty, l.note FROM @lines AS l;", LineList);
        var row = document.Types[Assert.IsType<IrRef>(Assert.IsType<IrArray>(document.Objects[0].Results![0].Type).Items).Name];

        Assert.Equal([false, false, true], row.Fields.Select(f => f.Nullable));
        Assert.Equal(["int", "smallint", "nvarchar(200)"], row.Fields.Select(f => Assert.IsType<IrScalar>(f.Type).Sql));
        Assert.Empty(document.Unknowns);
    }

    [Theory]
    [InlineData("WHERE l.product_id = @id", IrCardinality.AtMostOne)] // the primary key, fixed
    [InlineData("WHERE l.qty = 1", IrCardinality.Many)]
    [InlineData("", IrCardinality.Many)]
    public void A_query_that_fixes_a_table_valued_parameter_s_key_reads_one_row_at_most(string where, IrCardinality rows)
    {
        var document = WithTypes($"CREATE PROCEDURE shop.p @lines shop.line_list READONLY, @id int AS SELECT l.qty FROM @lines AS l {where};", LineList);

        Assert.Equal(rows, document.Objects[0].Results![0].Cardinality);
    }

    [Theory]
    [InlineData("UNIQUE (a, b)", "WHERE t.a = 1 AND t.b = 2", IrCardinality.AtMostOne)]
    [InlineData("UNIQUE (a, b)", "WHERE t.a = 1", IrCardinality.Many)] // a key needs every column fixed
    [InlineData("PRIMARY KEY (a) WITH (IGNORE_DUP_KEY = ON)", "WHERE t.a = 1", IrCardinality.AtMostOne)] // a duplicate is dropped, not kept
    [InlineData("INDEX ix UNIQUE (b)", "WHERE t.b = 1", IrCardinality.AtMostOne)]
    public void Every_key_a_table_type_declares_holds_of_its_parameter(string key, string where, IrCardinality rows)
    {
        var document = WithTypes(
            $"CREATE PROCEDURE shop.p @t shop.pairs READONLY AS SELECT t.a FROM @t AS t {where};",
            $"CREATE TYPE shop.pairs AS TABLE (a int NOT NULL, b int NOT NULL, {key});");

        Assert.Equal(rows, document.Objects[0].Results![0].Cardinality);
    }

    [Fact]
    public void A_key_that_ignores_duplicates_is_carried_as_such()
    {
        var document = WithTypes(
            "CREATE PROCEDURE shop.p @t shop.echo READONLY AS SELECT 1 AS one;",
            "CREATE TYPE shop.echo AS TABLE (k int NOT NULL PRIMARY KEY WITH (IGNORE_DUP_KEY = ON), v int NULL);");

        Assert.True(Assert.Single(document.TableTypes["shop.echo"].Keys).IgnoresDuplicates);
    }

    [Fact]
    public void A_check_s_value_set_holds_of_a_column_a_query_reads_from_a_table_valued_parameter()
    {
        var document = WithTypes(
            "CREATE PROCEDURE shop.p @tags shop.tag_list READONLY AS SELECT t.grade FROM @tags AS t;",
            "CREATE TYPE shop.tag_list AS TABLE (tag varchar(20) NOT NULL UNIQUE, grade tinyint NOT NULL DEFAULT 1 CHECK (grade IN (1, 2, 3)));");
        var grade = Assert.Single(document.Types[Assert.IsType<IrRef>(Assert.IsType<IrArray>(document.Objects[0].Results![0].Type).Items).Name].Fields);

        Assert.Equal(["1", "2", "3"], Assert.IsType<IrScalar>(grade.Type).Facets!.Values!.Items.Select(v => v.Text));
    }

    [Fact]
    public void A_table_variable_of_a_table_type_is_read_as_the_type_declares_it()
    {
        var document = WithTypes(
            """
            CREATE PROCEDURE shop.p @id int AS
            DECLARE @basket shop.line_list;
            INSERT INTO @basket (product_id, qty) VALUES (@id, 2);
            SELECT b.product_id, b.qty, b.note FROM @basket AS b WHERE b.product_id = @id;
            """,
            LineList);
        var result = document.Objects[0].Results![0];
        var row = document.Types[Assert.IsType<IrRef>(Assert.IsType<IrArray>(result.Type).Items).Name];

        Assert.Equal(IrCardinality.AtMostOne, result.Cardinality);
        Assert.Equal([false, false, true], row.Fields.Select(f => f.Nullable));
        Assert.Empty(document.Unknowns);

        // A variable is no parameter: the document defines no table type for it.
        Assert.Empty(document.TableTypes);
    }

    [Fact]
    public void A_table_type_named_without_its_schema_is_the_one_type_of_that_name()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @lines line_list READONLY AS SELECT l.qty FROM @lines AS l;", LineList);

        Assert.Equal("shop.line_list", Assert.Single(document.Objects[0].Parameters).TableType);
    }

    [Fact]
    public void Two_parameters_of_one_table_type_share_its_row_and_its_definition()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @a shop.line_list READONLY, @b shop.line_list READONLY AS SELECT 1 AS one;", LineList);
        var parameters = document.Objects[0].Parameters;

        Assert.Equal(RowOf(document, parameters[0]), RowOf(document, parameters[1]));
        Assert.Single(document.Types, t => t.Value.Identity?.Owner == IrTypeOwner.TableType);
        Assert.Single(document.TableTypes);
    }

    [Fact]
    public void A_parameter_of_an_alias_type_is_its_base_type()
    {
        var document = WithTypes("CREATE PROCEDURE shop.p @code shop.code AS SELECT @code AS code;", "CREATE TYPE shop.code FROM varchar(10) NOT NULL;");

        Assert.Equal("varchar(10)", Assert.IsType<IrScalar>(Assert.Single(document.Objects[0].Parameters).Type).Sql);
    }
}
