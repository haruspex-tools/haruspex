using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for <c>xml</c> values (decision 0133): when one is NULL, what its conversions and methods give and when
/// they are NULL, and how text and <c>xml</c> combine. Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>), values as a client reads them. The rules rest on these (Haruspex.SqlServer.Inference's
/// README.md, "XML values"). Observed on SQL Server 2025 RTM-CU10. What the mssql driver hands over for them is in
/// <c>MssqlDriverObservationTests</c>.
/// </summary>
public class XmlObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, params string[] setup) => ObservedCall.Run(server, setup, call);

    private Task<string> Describe(string select) => FirstResultSet.Describe(server, select);

    /// <summary>A pattern table: a draft never NULL, notes that may be, and a draft typed by a schema collection.</summary>
    private static readonly string[] Patterns =
    [
        """CREATE XML SCHEMA COLLECTION dbo.obs_draft AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="draft"><xs:complexType><xs:attribute name="shafts" type="xs:int"/></xs:complexType></xs:element></xs:schema>';""",
        "CREATE TABLE dbo.obs_pattern (id int NOT NULL PRIMARY KEY, draft xml NOT NULL, notes xml NULL, typed xml(dbo.obs_draft) NULL);",
        """INSERT dbo.obs_pattern VALUES (1, N'<draft shafts="4"/>', N'<notes by="Ada"/>', N'<draft shafts="4"/>'), (2, N'<draft/>', N'', NULL), (3, N'<other/>', NULL, NULL);""",
    ];

    // ---- values and conversions ------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_xml_column_is_null_only_where_it_is_and_an_empty_instance_is_not()
    {
        // Typed by a collection or not, a column is described as xml; an empty instance is text of no characters, not NULL.
        Assert.Equal(
            "draft xml not null, notes xml null, typed xml null",
            await FirstResultSet.DescribeNullability(server, "SELECT draft, notes, typed FROM dbo.obs_pattern", Patterns));
        Assert.Equal(
            "id,notes_is_null,notes_text: 1,0,<notes by=\"Ada\"/>;2,0,;3,1,null",
            (await Run("SELECT id, IIF(notes IS NULL, 1, 0) AS notes_is_null, CAST(notes AS nvarchar(max)) AS notes_text FROM dbo.obs_pattern ORDER BY id;", Patterns)).ToString());
    }

    [Fact]
    public async Task A_conversion_to_xml_is_null_only_for_null_and_try_forms_also_for_text_that_is_not_xml()
    {
        // CAST and CONVERT, with or without a style, are xml and NULL only for NULL; text that isn't well formed is an error (9400),
        // which TRY_CAST and TRY_CONVERT make NULL, and so is text too short for an instance converted out of xml (6354).
        Assert.Equal(
            "c xml, s xml, t xml, n nvarchar(max), v varchar(10)",
            await Describe("SELECT CAST(N'<a/>' AS xml) AS c, CONVERT(xml, N'<a>  <b/></a>', 1) AS s, TRY_CAST(N'<a' AS xml) AS t, CAST(CAST(N'<a/>' AS xml) AS nvarchar(max)) AS n, CAST(CAST(N'<a/>' AS xml) AS varchar(10)) AS v"));
        Assert.Equal("c,n,e: <a />,null,", (await Run("SELECT CAST(N'<a/>' AS xml) AS c, CAST(CAST(NULL AS nvarchar(10)) AS xml) AS n, CAST(N'' AS xml) AS e;")).ToString());
        Assert.Equal(9400, (await Run("SELECT CAST(N'<a' AS xml) AS c;")).Error);
        Assert.Equal(
            "t,u,s: null,null,null",
            (await Run("SELECT TRY_CAST(N'<a' AS xml) AS t, TRY_CONVERT(xml, N'<a>') AS u, TRY_CAST(CAST(N'<a>hello</a>' AS xml) AS nvarchar(3)) AS s;")).ToString());
    }

    [Fact]
    public async Task A_conversion_to_or_from_xml_fails_rather_than_giving_null_even_under_the_callers_options()
    {
        // With ANSI_WARNINGS and ARITHABORT off, where an overflow is NULL, text that isn't XML and an instance too long for its
        // target are still errors.
        const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; ";
        Assert.Equal(9400, (await Run(Off + "SELECT CAST(N'<a' AS xml) AS c;")).Error);
        Assert.Equal(6354, (await Run(Off + "SELECT CAST(CAST(N'<a>hello</a>' AS xml) AS nvarchar(3)) AS c;")).Error);
        Assert.Equal(6354, (await Run(Off + "SELECT CAST(CAST(N'<a>hello</a>' AS xml) AS varchar(3)) AS c;")).Error);
        Assert.Equal(6354, (await Run(Off + "SELECT CAST(CAST(N'<a>hello</a>' AS xml) AS varbinary(3)) AS c;")).Error);
        Assert.Equal(9400, (await Run(Off + "SELECT ISNULL(CAST(NULL AS xml), N'<b') AS c;")).Error);

        // An assignment of text to xml converts the same way; xml assigned to text is refused, since no conversion out of xml is
        // implicit (257).
        Assert.Equal(9400, (await Run(Off + "DECLARE @x xml = N'<a'; SELECT @x AS c;")).Error);
        Assert.Equal(257, (await Run(Off + "DECLARE @s nvarchar(30) = CAST(N'<a>hello</a>' AS xml); SELECT @s AS c;")).Error);
    }

    [Fact]
    public async Task Text_beside_xml_takes_xml_by_precedence()
    {
        // CASE, IIF, COALESCE, ISNULL and UNION ALL of xml and text are xml, the text parsed as XML; a branch not taken isn't.
        // SqlClient reads an instance as XML and writes it again, so an empty element is <a /> here.
        Assert.Equal(
            "i xml, c xml, k xml, f xml",
            await Describe("SELECT ISNULL(x.v, N'<b/>') AS i, COALESCE(x.v, N'<b/>') AS c, CASE WHEN 1 = 1 THEN x.v ELSE N'<b/>' END AS k, IIF(1 = 1, N'<c/>', x.v) AS f FROM (SELECT CAST(N'<a/>' AS xml) AS v) AS x"));
        Assert.Equal("u xml", await Describe("SELECT CAST(N'<a/>' AS xml) AS u UNION ALL SELECT N'<b/>'"));
        Assert.Equal("k: <a />", (await Run("SELECT CASE WHEN 1 = 1 THEN CAST(N'<a/>' AS xml) ELSE N'<b' END AS k;")).ToString());
        Assert.Equal("c: <b />", (await Run("SELECT COALESCE(CAST(NULL AS xml), N'<b/>') AS c;")).ToString());
    }

    [Fact]
    public async Task Xml_can_t_be_compared_so_min_max_nullif_and_string_agg_of_it_are_refused()
    {
        // 8117 for MIN and MAX, with or without a window; 305 for NULLIF and a percentile ordered by it; 8116 for STRING_AGG.
        const string From = " FROM (SELECT CAST(N'<a/>' AS xml) AS x, 1 AS id) AS t;";
        Assert.Equal(8117, (await Run("SELECT MIN(x) AS v" + From)).Error);
        Assert.Equal(8117, (await Run("SELECT MAX(x) OVER () AS v" + From)).Error);
        Assert.Equal(305, (await Run("SELECT NULLIF(x, x) AS v" + From)).Error);
        Assert.Equal(305, (await Run("SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY x) OVER () AS v" + From)).Error);
        Assert.Equal(8116, (await Run("SELECT STRING_AGG(x, ',') AS v" + From)).Error);
    }

    [Fact]
    public async Task An_unqualified_xml_schema_collection_is_found_as_an_alias_type_is()
    {
        // Outside a module, in the creator's default schema (here dbo), not the table's; in a module, its own schema's, then dbo's.
        string[] collections =
        [
            "CREATE SCHEMA obs_shop;",
            """CREATE XML SCHEMA COLLECTION obs_shop.obs_only_shop AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="a"/></xs:schema>';""",
            """CREATE XML SCHEMA COLLECTION dbo.obs_only_dbo AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="b"/></xs:schema>';""",
            """CREATE XML SCHEMA COLLECTION obs_shop.obs_both AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="c"/></xs:schema>';""",
            """CREATE XML SCHEMA COLLECTION dbo.obs_both AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="d"/></xs:schema>';""",
        ];
        const string Bound = "SELECT t.name AS t, x.name AS x, SCHEMA_NAME(x.schema_id) AS s FROM sys.columns AS c JOIN sys.tables AS t ON t.object_id = c.object_id JOIN sys.xml_schema_collections AS x ON x.xml_collection_id = c.xml_collection_id WHERE t.name LIKE 'obs_t%' ORDER BY t.name;";
        Assert.Equal(
            "t,x,s: obs_t2,obs_only_dbo,dbo;obs_t3,obs_both,dbo",
            (await Run(Bound, [.. collections, "CREATE TABLE obs_shop.obs_t2 (v xml(obs_only_dbo));", "CREATE TABLE obs_shop.obs_t3 (v xml(obs_both));"])).ToString());
        Assert.Equal(6314, (await Run("CREATE TABLE obs_shop.obs_t1 (v xml(obs_only_shop));", collections)).Error);

        // A procedure of obs_shop takes obs_shop's obs_both (an element c) over dbo's, and dbo's obs_only_dbo; one of dbo can't see
        // obs_shop's obs_only_shop.
        string[] procedures =
        [
            .. collections,
            "CREATE PROCEDURE obs_shop.obs_p1 AS BEGIN DECLARE @x xml(obs_both) = N'<c/>'; DECLARE @y xml(obs_only_dbo) = N'<b/>'; SELECT @x AS x, @y AS y; END;",
        ];
        Assert.Equal("x,y: <c />,<b />", (await Run("EXEC obs_shop.obs_p1;", procedures)).ToString());
        Assert.Equal(6314, (await Run("CREATE PROCEDURE dbo.obs_p2 AS BEGIN DECLARE @x xml(obs_only_shop) = N'<a/>'; SELECT @x AS x; END;", collections)).Error);
    }

    // ---- methods ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Xml_value_is_its_type_arguments_type_and_null_where_the_path_finds_no_node()
    {
        // Read as a declaration reads a type: nvarchar is nvarchar(1), decimal decimal(18,0), in any case, bracketed or in sys,
        // with an N literal too. Always described as nullable, over a column never NULL too.
        Assert.Equal(
            "a int, b decimal(9,2), c nvarchar(1), d decimal(18,0), e nvarchar(20), f int, g int, h varchar(20), i datetime2(7), j bit, k varbinary(10), l char(1), m uniqueidentifier",
            await Describe("SELECT d.draft.value('(/draft/@shafts)[1]', 'int') AS a, d.draft.value('(/a)[1]', 'decimal(9,2)') AS b, d.draft.value('(/a)[1]', 'nvarchar') AS c, d.draft.value('(/a)[1]', 'decimal') AS d, d.draft.value('(/a)[1]', 'NVARCHAR(20)') AS e, d.draft.value('(/a)[1]', '[int]') AS f, d.draft.value('(/a)[1]', 'sys.int') AS g, d.draft.value('(/a)[1]', N'varchar(20)') AS h, d.draft.value('(/a)[1]', 'datetime2') AS i, d.draft.value('(/a)[1]', 'bit') AS j, d.draft.value('(/a)[1]', 'varbinary(10)') AS k, d.draft.value('(/a)[1]', 'char') AS l, d.draft.value('(/a)[1]', 'uniqueidentifier') AS m FROM (SELECT CAST(N'<draft/>' AS xml) AS draft) AS d"));
        Assert.Equal("v int null", await FirstResultSet.DescribeNullability(server, "SELECT draft.value('(/draft/@shafts)[1]', 'int') AS v FROM dbo.obs_pattern", Patterns));

        // NULL where the path finds no node and where the XML is NULL; a longer value is cut to the type's length.
        Assert.Equal(
            "id,shafts,noted_by,cut: 1,4,Ada,Ad;2,null,null,null;3,null,null,null",
            (await Run("SELECT id, draft.value('(/draft/@shafts)[1]', 'int') AS shafts, notes.value('(/notes/@by)[1]', 'nvarchar(10)') AS noted_by, notes.value('(/notes/@by)[1]', 'varchar(2)') AS cut FROM dbo.obs_pattern ORDER BY id;", Patterns)).ToString());
    }

    [Fact]
    public async Task Xml_value_of_json_or_vector_is_typed_by_what_the_client_supports_and_other_types_are_refused()
    {
        // json and vector(n) are taken, and given to a client that supports them, as SqlClient does, as those types; to one that
        // doesn't, as sqlcmd's ODBC driver didn't when tried by hand, as varchar(max). The others below are refused when the query
        // is compiled.
        Assert.Equal(
            "j json, v vector(3)",
            await Describe("SELECT x.v.value('(/a)[1]', 'json') AS j, x.v.value('(/a)[1]', 'vector(3)') AS v FROM (SELECT CAST(N'<a/>' AS xml) AS v) AS x"));
        foreach (var type in new[] { "xml", "text", "ntext", "image", "sql_variant", "hierarchyid", "geography", "dbo.no_such_type" })
            Assert.Equal(9500, (await Run($"SELECT CAST(N'<a/>' AS xml).value('(/a)[1]', '{type}') AS v;")).Error);
    }

    [Fact]
    public async Task Xml_methods_take_only_literal_arguments()
    {
        // A variable or any other expression as the path or the type is refused when the query is compiled (8172).
        Assert.Equal(8172, (await Run("DECLARE @t nvarchar(10) = N'int'; SELECT CAST(N'<a/>' AS xml).value('(/a)[1]', @t) AS v;")).Error);
        Assert.Equal(8172, (await Run("DECLARE @p nvarchar(10) = N'/a'; SELECT CAST(N'<a/>' AS xml).value(@p, 'int') AS v;")).Error);
        Assert.Equal(8172, (await Run("SELECT CAST(N'<a/>' AS xml).value('(/a)[1]', 'int' + '') AS v;")).Error);
        Assert.Equal(8172, (await Run("DECLARE @p nvarchar(10) = N'/a'; SELECT CAST(N'<a/>' AS xml).exist(@p) AS v;")).Error);
    }

    [Fact]
    public async Task Xml_exist_is_a_bit_null_exactly_when_the_xml_is()
    {
        // 1 where the path finds a node, 0 where it finds none, an empty instance included, and where a NULL sql:variable or
        // sql:column makes the path find nothing; NULL only for NULL XML.
        Assert.Equal("e bit", await Describe("SELECT CAST(N'<a/>' AS xml).exist('/a') AS e"));
        Assert.Equal(
            "id,has_notes,has_by,draft_found: 1,True,True,True;2,False,False,True;3,null,null,True",
            (await Run("SELECT id, notes.exist('/notes') AS has_notes, notes.exist('/notes/@by') AS has_by, draft.exist('/*') AS draft_found FROM dbo.obs_pattern ORDER BY id;", Patterns)).ToString());
        Assert.Equal(
            "v,c: False,False",
            (await Run("""DECLARE @x xml = N'<a b="1"/>', @n int = NULL; SELECT @x.exist('/a[@b=sql:variable("@n")]') AS v, @x.exist('/a[@b=sql:column("k.k")]') AS c FROM (VALUES (CAST(NULL AS int))) AS k(k);""")).ToString());
    }

    [Fact]
    public async Task A_method_of_a_column_named_as_a_schema_with_a_function_of_that_name_is_refused_as_ambiguous()
    {
        // spec.value(…) where spec is a column in scope and a function spec.value exists is refused (327); without the function,
        // it is the column's method.
        string[] setup = [.. Patterns, "CREATE SCHEMA draft;", "CREATE FUNCTION draft.value(@a nvarchar(50), @b nvarchar(50)) RETURNS int AS BEGIN RETURN 42; END;"];
        Assert.Equal(327, (await Run("SELECT draft.value('(/draft/@shafts)[1]', 'int') AS v FROM dbo.obs_pattern;", setup)).Error);
        Assert.Equal("v: 4", (await Run("SELECT draft.value('(/draft/@shafts)[1]', 'int') AS v FROM dbo.obs_pattern WHERE id = 1;", Patterns)).ToString());
    }
}
