using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server gives for XML's expressions beyond its values (decision 0137): <c>nodes()</c> as a source, <c>query()</c>,
/// <c>FOR XML</c> subqueries and <c>OPENXML … WITH</c>, their types as the server describes them and their values and NULLs as a
/// client reads them. The rules rest on these (Haruspex.SqlServer.Inference's README.md, "XML values"). Observed on SQL Server
/// 2025 RTM-CU10. <c>XmlObservationTests</c> holds the values and the other methods.
/// </summary>
public class XmlExpressionObservationTests(SqlServerFixture server)
{
    private Task<CallOutcome> Run(string call, params string[] setup) => ObservedCall.Run(server, setup, call);

    private Task<string> Describe(string select, params string[] setup) => FirstResultSet.Describe(server, select, setup);

    /// <summary>Orders with lines as XML: two lines, none, a NULL instance and an empty one.</summary>
    private static readonly string[] Orders =
    [
        "CREATE TABLE dbo.obs_order (id int NOT NULL PRIMARY KEY, lines xml NULL);",
        """INSERT dbo.obs_order VALUES (1, N'<lines><line sku="a1"/><line sku="b2"/></lines>'), (2, N'<lines/>'), (3, NULL), (4, N'');""",
    ];

    /// <summary>Products whose names hold characters XML escapes.</summary>
    private static readonly string[] Products =
    [
        "CREATE TABLE dbo.obs_product (id int NOT NULL PRIMARY KEY, name nvarchar(20) NOT NULL, colour varchar(10) NULL);",
        "INSERT dbo.obs_product VALUES (1, N'nuts & bolts', 'red'), (2, N'<washer>', NULL);",
    ];

    // ---- nodes() ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Nodes_gives_a_row_for_each_node_and_none_for_null()
    {
        // CROSS APPLY gives a row for each node the path finds: none for a path that finds nothing, a NULL instance or an
        // empty one. OUTER APPLY keeps each of those outer rows, its node NULL.
        Assert.Equal(
            "id,sku: 1,a1;1,b2",
            (await Run("SELECT o.id, n.c.value('@sku', 'varchar(5)') AS sku FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines/line') AS n(c) ORDER BY o.id, sku;", Orders)).ToString());
        Assert.Equal(
            "id,missing: 1,0;1,0;2,1;3,1;4,1",
            (await Run("SELECT o.id, IIF(n.c IS NULL, 1, 0) AS missing FROM dbo.obs_order AS o OUTER APPLY o.lines.nodes('/lines/line') AS n(c) ORDER BY o.id, missing;", Orders)).ToString());
        Assert.Equal(
            "some_rows,no_rows: 2,0",
            (await Run("""
                DECLARE @some xml = N'<r><i/><i/></r>', @none xml = NULL;
                SELECT (SELECT COUNT(*) FROM @some.nodes('/r/i') AS t(c)) AS some_rows, (SELECT COUNT(*) FROM @none.nodes('/r/i') AS t(c)) AS no_rows;
                """)).ToString());
    }

    [Fact]
    public async Task A_nodes_column_is_never_null_and_is_read_only_by_xml_s_methods()
    {
        // Its node is never NULL on a row CROSS APPLY gives: exist('.') is 1 and query('.') the node. value() still finds
        // nothing at a path the node lacks.
        Assert.Equal(
            "here,node,size: True,<line sku=\"a1\"/>,null;True,<line sku=\"b2\"/>,null",
            (await Run("SELECT n.c.exist('.') AS here, CAST(n.c.query('.') AS nvarchar(max)) AS node, n.c.value('@size', 'int') AS size FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines/line') AS n(c) ORDER BY node;", Orders)).ToString());
        Assert.Equal(
            "v xml, e bit, s varchar(5)",
            await Describe("SELECT n.c.query('.') AS v, n.c.exist('.') AS e, n.c.value('@sku', 'varchar(5)') AS s FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines/line') AS n(c)", Orders));

        // Anything else is refused: the column alone, *, a conversion, GROUP BY, COUNT.
        const string From = " FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines/line') AS n(c);";
        Assert.Equal(493, (await Run("SELECT n.c" + From, Orders)).Error);
        Assert.Equal(493, (await Run("SELECT *" + From, Orders)).Error);
        Assert.Equal(525, (await Run("SELECT CAST(n.c AS nvarchar(max)) AS c" + From, Orders)).Error);
        Assert.Equal(493, (await Run("SELECT COUNT(n.c) AS c" + From, Orders)).Error);
        Assert.Equal(493, (await Run("SELECT n.c.exist('.') AS c FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines/line') AS n(c) GROUP BY n.c;", Orders)).Error);
    }

    [Fact]
    public async Task Nodes_takes_one_column_a_literal_path_and_only_xml()
    {
        // Two columns in its list (8159), none (208: read as a function), a path that isn't a literal (8172), and a value
        // that isn't xml (9506) are refused.
        Assert.Equal(8159, (await Run("SELECT 1 AS c FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines') AS n(c, d);", Orders)).Error);
        Assert.Equal(208, (await Run("SELECT 1 AS c FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes('/lines') AS n;", Orders)).Error);
        Assert.Equal(8172, (await Run("DECLARE @p nvarchar(10) = N'/lines'; SELECT 1 AS c FROM dbo.obs_order AS o CROSS APPLY o.lines.nodes(@p) AS n(c);", Orders)).Error);
        Assert.Equal(9506, (await Run("DECLARE @s nvarchar(10) = N'<a/>'; SELECT 1 AS c FROM @s.nodes('/a') AS n(c);")).Error);
    }

    // ---- query() ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Query_is_untyped_xml_null_exactly_when_its_instance_is()
    {
        // Where the path finds nothing it is an empty instance, not NULL; NULL only for a NULL instance.
        Assert.Equal(
            "id,found,none: 1,<line sku=\"a1\"/><line sku=\"b2\"/>,;2,,;3,null,null;4,,",
            (await Run("SELECT id, CAST(lines.query('/lines/line') AS nvarchar(max)) AS found, CAST(lines.query('/nothing') AS nvarchar(max)) AS none FROM dbo.obs_order ORDER BY id;", Orders)).ToString());
        Assert.Equal("q xml", await Describe("SELECT lines.query('/lines') AS q FROM dbo.obs_order", Orders));
        Assert.Equal(8172, (await Run("DECLARE @p nvarchar(10) = N'/lines'; SELECT lines.query(@p) AS q FROM dbo.obs_order;", Orders)).Error);
    }

    // ---- FOR XML subqueries ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_for_xml_subquery_is_xml_with_type_and_nvarchar_max_without_and_null_only_over_no_rows()
    {
        // TYPE makes it xml, in every mode; without TYPE it is nvarchar(max), ROOT and ELEMENTS included.
        Assert.Equal(
            "a xml, b nvarchar(max), c nvarchar(max), d xml, e nvarchar(max), f nvarchar(max), g xml, h nvarchar(max), i nvarchar(max)",
            await Describe("""
                SELECT (SELECT name FROM dbo.obs_product FOR XML PATH(''), TYPE) AS a, (SELECT name FROM dbo.obs_product FOR XML PATH('')) AS b,
                       (SELECT name FROM dbo.obs_product FOR XML RAW) AS c, (SELECT name FROM dbo.obs_product FOR XML RAW, TYPE) AS d,
                       (SELECT name FROM dbo.obs_product FOR XML AUTO, ELEMENTS) AS e, (SELECT name FROM dbo.obs_product FOR XML PATH('p'), ROOT('ps')) AS f,
                       (SELECT name FROM dbo.obs_product FOR XML PATH('p'), ROOT('ps'), TYPE) AS g,
                       (SELECT 1 AS tag, NULL AS parent, name AS [p!1!name] FROM dbo.obs_product FOR XML EXPLICIT) AS h,
                       STUFF((SELECT ',' + name FROM dbo.obs_product FOR XML PATH('')), 1, 1, '') AS i
                """,
                Products));

        // NULL over no rows, a ROOT included; over a row it is never NULL, even when every column is: '' without TYPE, an
        // empty instance with it, and a bare element in RAW and PATH.
        Assert.Equal(
            "path,typed,root,raw_root,rows_null,typed_null,raw_null: null,null,null,null,,0,<row/>",
            (await Run("""
                SELECT (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML PATH('')) AS path,
                       (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML PATH(''), TYPE) AS typed,
                       (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML PATH('p'), ROOT('ps')) AS root,
                       (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML RAW, ROOT('ps'), TYPE) AS raw_root,
                       (SELECT colour FROM dbo.obs_product WHERE id = 2 FOR XML PATH('')) AS rows_null,
                       IIF((SELECT NULL AS n FOR XML PATH(''), TYPE) IS NULL, 1, 0) AS typed_null,
                       (SELECT NULL AS n FOR XML RAW) AS raw_null;
                """, Products)).ToString());
    }

    [Fact]
    public async Task A_for_xml_derived_table_or_cte_is_one_row_of_what_the_query_builds()
    {
        // Exactly one row, over no rows too, whose column is the subquery's value: nvarchar(max), or xml with TYPE, NULL over no
        // rows. Its column needs a name from the column list (8155).
        Assert.Equal(
            "x nvarchar(max)",
            await Describe("SELECT d.x FROM (SELECT name FROM dbo.obs_product FOR XML PATH('')) AS d (x)", Products));
        Assert.Equal(
            "x xml",
            await Describe("WITH c (x) AS (SELECT name FROM dbo.obs_product FOR XML PATH(''), TYPE) SELECT c.x FROM c", Products));
        Assert.Equal(
            "n,missing: 1,0 | n,missing: 1,1 | n,missing: 1,1",
            (await Run("""
                SELECT COUNT(*) AS n, MAX(IIF(d.x IS NULL, 1, 0)) AS missing FROM (SELECT name FROM dbo.obs_product FOR XML PATH('')) AS d (x);
                SELECT COUNT(*) AS n, MAX(IIF(d.x IS NULL, 1, 0)) AS missing FROM (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML RAW, ROOT('r')) AS d (x);
                WITH c (x) AS (SELECT name FROM dbo.obs_product WHERE id = 0 FOR XML PATH(''), TYPE) SELECT COUNT(*) AS n, MAX(IIF(c.x IS NULL, 1, 0)) AS missing FROM c;
                """, Products)).ToString());
        Assert.Equal(8155, (await Run("SELECT d.name FROM (SELECT name FROM dbo.obs_product FOR XML PATH('')) AS d;", Products)).Error);
    }

    [Fact]
    public async Task The_stuff_idiom_escapes_what_xml_escapes_and_is_null_over_no_rows()
    {
        // Without TYPE the text is XML's: & < > escaped, a matter of the value, not its type. With TYPE and value() it isn't.
        // Over no rows the subquery is NULL, and so is STUFF of it.
        Assert.Equal(
            "plain,typed,none: nuts &amp; bolts,&lt;washer&gt;,nuts & bolts,<washer>,null",
            (await Run("""
                SELECT STUFF((SELECT ',' + name FROM dbo.obs_product ORDER BY id FOR XML PATH('')), 1, 1, '') AS plain,
                       STUFF((SELECT ',' + name FROM dbo.obs_product ORDER BY id FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, '') AS typed,
                       STUFF((SELECT ',' + name FROM dbo.obs_product WHERE id = 0 FOR XML PATH('')), 1, 1, '') AS none;
                """, Products)).ToString());
    }

    // ---- OPENXML ---------------------------------------------------------------------------------------------------------

    /// <summary>A document prepared into <c>@h</c>: a full item, one whose values don't convert or don't fit, and an empty one.</summary>
    private const string Prepared = """
        DECLARE @h int;
        EXEC sp_xml_preparedocument @h OUTPUT, N'<r><i sku="a1" size="large" qty="3" price="2.55"><note>new</note></i><i sku="zz" qty="many" price="12345.6"/><i/></r>';
        """;

    [Fact]
    public async Task Openxml_with_columns_are_their_declared_types_converted_as_cast_converts_text()
    {
        // Each column is the type it declares; NULL where its path finds nothing. A string is cut short silently and a char
        // padded; a decimal is rounded.
        Assert.Equal(
            "a varchar(5), b nvarchar(3), c int, d numeric(5,1), e char(4), f xml, g datetime2(2), h uniqueidentifier, i varbinary(max)",
            await Describe("DECLARE @h int; SELECT * FROM OPENXML(@h, '/r/i', 1) WITH (a varchar(5), b nvarchar(3), c int, d decimal(5,1), e char(4), f xml '.', g datetime2(2), h uniqueidentifier, i varbinary(max))"));
        Assert.Equal(
            "sku,size,code,price,note: a1,lar,[a1  ],2.6,new;zz,null,[zz  ],12345.6,null;null,null,null,null,null",
            (await Run(Prepared + """
                SELECT x.sku, x.size, '[' + x.code + ']' AS code, x.price, x.note
                FROM OPENXML(@h, '/r/i', 1) WITH (sku varchar(5), size varchar(3), code char(4) '@sku', price decimal(6, 1), note varchar(10) 'note') AS x;
                EXEC sp_xml_removedocument @h;
                """)).ToString());

        // Text that doesn't convert is an error, whatever the options; an overflow (12345.6) is an error, or NULL under
        // ANSI_WARNINGS and ARITHABORT off, and so is a rounding (2.55) under NUMERIC_ROUNDABORT on then.
        Assert.Equal(245, (await Run(Prepared + "SELECT qty FROM OPENXML(@h, '/r/i', 1) WITH (qty int);")).Error);
        Assert.Equal(245, (await Run(Prepared + "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT qty FROM OPENXML(@h, '/r/i', 1) WITH (qty int);")).Error);
        Assert.Equal(8115, (await Run(Prepared + "SELECT price FROM OPENXML(@h, '/r/i', 1) WITH (price decimal(4, 1));")).Error);
        Assert.Equal(
            "price: 2.6;null;null",
            (await Run(Prepared + "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SELECT price FROM OPENXML(@h, '/r/i', 1) WITH (price decimal(4, 1));")).ToString());
        Assert.Equal(
            "price: null",
            (await Run(Prepared + "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; SET NUMERIC_ROUNDABORT ON; SELECT price FROM OPENXML(@h, '/r/i[1]', 1) WITH (price decimal(4, 1));")).ToString());
    }

    [Fact]
    public async Task Openxml_with_a_table_s_name_is_null_where_its_path_finds_nothing_whatever_the_table_declares()
    {
        // WITH a table's name takes its columns, but a column the table declares NOT NULL is still NULL for a missing node.
        Assert.Equal(
            "sku,size: a1,lar;zz,null;null,null",
            (await Run(Prepared + "SELECT * FROM OPENXML(@h, '/r/i', 1) WITH dbo.obs_shape;", "CREATE TABLE dbo.obs_shape (sku varchar(5) NOT NULL, size varchar(3) NULL);")).ToString());
    }

    [Fact]
    public async Task Openxml_without_with_is_the_edge_table()
    {
        Assert.Equal(
            "id bigint, parentid bigint, nodetype int, localname nvarchar(4000), prefix nvarchar(4000), namespaceuri nvarchar(4000), datatype nvarchar(4000), prev bigint, text ntext",
            await Describe("DECLARE @h int; SELECT * FROM OPENXML(@h, '/r/i')"));
    }

    [Fact]
    public async Task Sp_xml_preparedocument_and_sp_xml_removedocument_return_no_result_set()
    {
        // Each returns no result set and 0; removing a handle no document has is error 8179 and 1. XML that isn't well formed
        // is error 6602.
        Assert.Equal(
            "prepared,removed,again: 0,0,1 ~ 8179",
            (await ObservedCall.Run(server, [], """
                DECLARE @h int, @prepared int, @removed int, @again int;
                EXEC @prepared = sp_xml_preparedocument @h OUTPUT, N'<r/>';
                EXEC @removed = sp_xml_removedocument @h;
                EXEC @again = sp_xml_removedocument @h;
                SELECT @prepared AS prepared, @removed AS removed, @again AS again;
                """, errorsAsMessages: true)).ToString());
        Assert.Equal(6602, (await Run("DECLARE @h int; EXEC sp_xml_preparedocument @h OUTPUT, N'<r>';")).Error);
    }
}
