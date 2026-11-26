using Haruspex.Ir;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;
using static Haruspex.TestSupport.IrNavigation;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>CONTAINSTABLE</c> and <c>FREETEXTTABLE</c> as sources: <c>KEY</c> of the type of the table's full-text key column, followed
/// from <c>CREATE FULLTEXT INDEX … KEY INDEX</c> to that index's one column, and <c>RANK</c> an <c>int</c> the server declares
/// nullable. The engine rules are observed in the differential tests' <c>FullTextObservationTests</c>, on a server with full-text
/// search, named where they matter.
/// </summary>
public class FullTextTests
{
    private const string Schema = """
        CREATE TYPE dbo.label_ref FROM char(8) NOT NULL;
        GO
        CREATE TABLE dbo.entry (id int NOT NULL CONSTRAINT pk_entry PRIMARY KEY, headword nvarchar(100) NOT NULL, definition nvarchar(max) NULL);
        GO
        CREATE FULLTEXT INDEX ON dbo.entry (headword, definition) KEY INDEX pk_entry;
        GO
        CREATE TABLE dbo.note (id int NOT NULL CONSTRAINT pk_note PRIMARY KEY, code varchar(10) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL, entry_id int NULL, body nvarchar(400) NULL);
        GO
        CREATE UNIQUE INDEX ux_note_code ON dbo.note (code);
        GO
        CREATE FULLTEXT INDEX ON dbo.note (body) KEY INDEX UX_NOTE_CODE;
        GO
        CREATE TABLE dbo.label (ref dbo.label_ref CONSTRAINT uq_label_ref UNIQUE, words nvarchar(200) NULL);
        GO
        CREATE FULLTEXT INDEX ON dbo.label (words) KEY INDEX uq_label_ref;
        GO
        CREATE TABLE dbo.draft (id int NOT NULL CONSTRAINT pk_draft PRIMARY KEY, words nvarchar(200) NULL);
        GO
        CREATE TABLE dbo.missing_key (id int NOT NULL PRIMARY KEY, words nvarchar(200) NULL);
        GO
        CREATE FULLTEXT INDEX ON dbo.missing_key (words) KEY INDEX ux_nowhere;
        GO
        CREATE TABLE dbo.pair (a int NOT NULL, b int NOT NULL, words nvarchar(200) NULL, CONSTRAINT ux_pair UNIQUE (a, b));
        GO
        CREATE FULLTEXT INDEX ON dbo.pair (words) KEY INDEX ux_pair;
        GO
        CREATE TABLE dbo.twice (id int NOT NULL CONSTRAINT pk_twice PRIMARY KEY, code nchar(4) NOT NULL CONSTRAINT uq_twice UNIQUE, words nvarchar(200) NULL);
        GO
        CREATE FULLTEXT INDEX ON dbo.twice (words) KEY INDEX pk_twice;
        GO
        DROP FULLTEXT INDEX ON dbo.twice;
        GO
        CREATE FULLTEXT INDEX ON dbo.twice (words) KEY INDEX uq_twice;
        GO
        CREATE TABLE dbo.again (id bigint NOT NULL CONSTRAINT pk_again PRIMARY KEY, words nvarchar(200) NULL);
        GO
        CREATE FULLTEXT INDEX ON dbo.again (words) KEY INDEX pk_again;
        GO
        DROP FULLTEXT INDEX ON dbo.again;
        GO
        CREATE FULLTEXT INDEX ON dbo.again (words) KEY INDEX PK_AGAIN;
        GO
        CREATE TABLE dbo.dropped (id int NOT NULL, words nvarchar(200) NULL);
        GO
        CREATE UNIQUE INDEX ux_dropped ON dbo.dropped (id);
        GO
        CREATE FULLTEXT INDEX ON dbo.dropped (words) KEY INDEX ux_dropped;
        GO
        DROP INDEX ux_dropped ON dbo.dropped;
        GO
        CREATE TABLE dbo.computed (a int NOT NULL, c AS a + 1 PERSISTED NOT NULL, words nvarchar(200) NULL);
        GO
        CREATE UNIQUE INDEX ux_computed ON dbo.computed (c);
        GO
        CREATE FULLTEXT INDEX ON dbo.computed (words) KEY INDEX ux_computed;
        GO
        CREATE VIEW dbo.entry_view AS SELECT e.id, e.headword FROM dbo.entry AS e;
        GO
        CREATE SCHEMA other;
        GO
        CREATE TABLE other.entry (code char(8) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL CONSTRAINT pk_other_entry PRIMARY KEY, words nvarchar(10) NULL);
        GO
        CREATE FULLTEXT INDEX ON other.entry (words) KEY INDEX pk_other_entry;
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("dbo.p", ("schema.sql", Schema), ("p.sql", $"CREATE PROCEDURE dbo.p @q nvarchar(400) AS\n{body}"));

    /// <summary>The field's SQL type and its reasons to be null, or <c>unknown: reason</c>.</summary>
    private static string Typed(IrDocument doc, string name)
    {
        var field = Field(doc, name);
        var type = field.Type is IrScalar scalar
            ? scalar.Sql ?? "(none)"
            : "unknown: " + IrWireNames.Of(Unknown(doc, doc.Unknowns.Single(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Path).Reason);
        return string.Join(" ", [type, .. field.NullableBecause.Select(r => IrWireNames.Of(r.Rule))]);
    }

    /// <summary>The detail of the one unknown at the field, which says why.</summary>
    private static string Why(IrDocument doc, string name) =>
        Assert.Single(doc.Unknowns, u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Detail;

    // ---- KEY and RANK ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Containstable_gives_key_of_its_primary_key_s_type_never_null_and_rank_an_int_declared_nullable()
    {
        // Observed: A_full_text_search_declares_key_as_its_key_column_never_null_and_rank_an_int_it_may_make_null.
        var doc = Procedure("SELECT ft.[KEY] AS k, ft.[RANK] AS r FROM CONTAINSTABLE(dbo.entry, (headword, definition), @q, LANGUAGE 1033, 10) AS ft;");

        Assert.Equal("int", Typed(doc, "k"));
        Assert.Equal("int nullable-column", Typed(doc, "r"));
        Assert.Equal("CONTAINSTABLE.RANK", Assert.Single(Field(doc, "r").NullableBecause).At);
    }

    [Fact]
    public void Freetexttable_gives_key_of_the_column_of_a_unique_index_created_apart_in_its_collation()
    {
        // The key index is named in another case than its declaration, which SQL Server takes (observed in the same test).
        var doc = Procedure("SELECT ft.[KEY] AS k, ft.[RANK] AS r FROM FREETEXTTABLE(dbo.note, *, @q) AS ft;");

        Assert.Equal("varchar(10)", Typed(doc, "k"));
        var key = (IrScalar)Field(doc, "k").Type;
        Assert.Equal(10, key.Facets?.Length?.Value);
        Assert.Equal(IrLengthUnit.Bytes, key.Facets?.Length?.Unit);
        Assert.Equal("int nullable-column", Typed(doc, "r"));
    }

    [Fact]
    public void Key_follows_its_index_to_that_index_s_column_and_not_to_the_primary_key()
    {
        // dbo.note's primary key is an int; its full-text key is ux_note_code, on the varchar code.
        var doc = Procedure("SELECT ft.[KEY] AS k FROM FREETEXTTABLE(dbo.note, body, @q) AS ft;");

        Assert.Equal("varchar(10)", Typed(doc, "k"));
    }

    [Fact]
    public void Key_of_a_column_of_an_alias_type_is_of_its_base_type()
    {
        // Observed: the server describes it as char(8), the alias type's base.
        var doc = Procedure("SELECT ft.[KEY] AS k FROM CONTAINSTABLE(dbo.label, words, @q) AS ft;");

        Assert.Equal("char(8)", Typed(doc, "k"));
        Assert.True(((IrScalar)Field(doc, "k").Type).Facets?.Length?.IsFixed);
    }

    [Fact]
    public void A_schema_qualified_search_reads_that_schema_s_table_not_one_of_the_same_name_in_the_procedure_s_schema()
    {
        // dbo.p's schema has dbo.entry, keyed by an int; other.entry is keyed by a char(8). Observed in the glossary's
        // lex.search_archive (FullTextObservationTests), whose KEY the server returns as archive.entry's.
        var doc = Procedure("SELECT ft.[KEY] AS k FROM CONTAINSTABLE(other.entry, words, @q) AS ft;");

        Assert.Equal("char(8)", Typed(doc, "k"));
    }

    [Fact]
    public void Key_keeps_its_column_s_own_collation_not_the_database_s()
    {
        // A char(8) in a UTF-8 collation is 8 bytes of UTF-8, which are fewer characters when any isn't ASCII: in the database's
        // collation, or a single-byte one, it would be 8 characters (observed: the glossary's archive.entry holding éé comes
        // back as 6 characters). KEY's length facet is the column's own.
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("schema.sql", Schema),
            ("p.sql", "CREATE PROCEDURE dbo.p @q nvarchar(400) AS\nSELECT ft.[KEY] AS k, e.code FROM CONTAINSTABLE(other.entry, words, @q) AS ft JOIN other.entry AS e ON e.code = ft.[KEY];"));

        var key = (IrScalar)Field(doc, "k").Type;
        Assert.Equal(((IrScalar)Field(doc, "code").Type).Facets, key.Facets);
        Assert.Equal(IrByteEncoding.Utf8, key.Facets?.Length?.Encoding);
    }

    [Fact]
    public void A_search_without_an_alias_is_known_by_its_table_s_name()
    {
        // Observed: A_full_text_search_without_an_alias_is_known_by_its_tables_name.
        var doc = Procedure("SELECT entry.[KEY] AS k, [RANK] AS r FROM CONTAINSTABLE(dbo.entry, *, @q);");

        Assert.Equal("int", Typed(doc, "k"));
        Assert.Equal("int nullable-column", Typed(doc, "r"));
    }

    [Fact]
    public void A_search_on_an_outer_join_s_optional_side_may_have_no_row()
    {
        var doc = Procedure("""
            SELECT e.id, ft.[KEY] AS k, ft.[RANK] AS r
            FROM dbo.entry AS e LEFT JOIN CONTAINSTABLE(dbo.entry, headword, @q) AS ft ON ft.[KEY] = e.id;
            """);

        Assert.Equal("int unmatched-row", Typed(doc, "k"));
        Assert.Equal("int nullable-column unmatched-row", Typed(doc, "r"));
    }

    [Fact]
    public void A_search_joined_to_its_table_keeps_the_table_s_columns_as_declared()
    {
        var doc = Procedure("""
            SELECT e.id, e.headword, ft.[RANK] AS r
            FROM dbo.entry AS e JOIN CONTAINSTABLE(dbo.entry, headword, @q) AS ft ON ft.[KEY] = e.id
            ORDER BY ft.[RANK] DESC;
            """);

        Assert.Equal("int", Typed(doc, "id"));
        Assert.Equal("nvarchar(100)", Typed(doc, "headword"));
        Assert.Equal("int nullable-column", Typed(doc, "r"));
    }

    [Fact]
    public void A_filter_on_rank_proves_it_not_null()
    {
        var doc = Procedure("SELECT ft.[RANK] AS r FROM CONTAINSTABLE(dbo.entry, headword, @q) AS ft WHERE ft.[RANK] > 10;");

        Assert.Equal("int", Typed(doc, "r"));
    }

    [Fact]
    public void A_full_text_index_created_again_with_the_same_key_in_another_case_still_gives_key()
    {
        var doc = Procedure("SELECT ft.[KEY] AS k FROM CONTAINSTABLE(dbo.again, words, @q) AS ft;");

        Assert.Equal("bigint", Typed(doc, "k"));
    }

    // ---- KEY unknown, RANK still typed ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("dbo.draft", "creates no full-text index on dbo.draft")]
    [InlineData("dbo.missing_key", "no unique index, unique constraint or primary key named ux_nowhere")]
    [InlineData("dbo.pair", "has more than one column")]
    [InlineData("dbo.twice", "more than once, with the keys pk_twice and uq_twice")]
    [InlineData("dbo.dropped", "may be dropped or rebuilt")]
    [InlineData("dbo.computed", "is computed, masked or not in the table")]
    [InlineData("dbo.entry_view", "is not a table of this database")]
    [InlineData("dbo.nowhere", "There is no table dbo.nowhere")]
    [InlineData("other.dbo.entry", "to another database is not modelled yet")]
    [InlineData("#entry", "is a temporary table, which can't have a full-text index")]
    public void Key_without_a_key_column_to_follow_is_unknown_and_rank_is_still_typed(string table, string why)
    {
        var doc = Procedure($"SELECT ft.[KEY] AS k, ft.[RANK] AS r FROM CONTAINSTABLE({table}, *, @q) AS ft;");

        Assert.StartsWith("unknown: ", Typed(doc, "k"), StringComparison.Ordinal);
        Assert.Contains(why, Why(doc, "k"), StringComparison.Ordinal);
        Assert.Equal("int nullable-column", Typed(doc, "r"));
    }

    [Theory]
    [InlineData("lex.p", "bigint")]
    [InlineData("dbo.p", "int")]
    public void A_one_part_name_searches_the_table_its_procedure_s_schema_has_first(string procedure, string key)
    {
        // As a one-part name in FROM resolves: the procedure's own schema, then dbo.
        static string Body(string name) => $"CREATE PROCEDURE {name} @q nvarchar(400) AS\nSELECT ft.[KEY] AS k FROM CONTAINSTABLE(entry, *, @q) AS ft;";
        var doc = InferProcedure.Infer(
            procedure,
            ("schema.sql", Schema + "\nCREATE SCHEMA lex;\nGO\nCREATE TABLE lex.entry (id bigint NOT NULL CONSTRAINT pk_lex_entry PRIMARY KEY, words nvarchar(10) NULL);\nGO\nCREATE FULLTEXT INDEX ON lex.entry (words) KEY INDEX pk_lex_entry;\nGO\n"),
            ("lex-p.sql", Body("lex.p")),
            ("dbo-p.sql", Body("dbo.p")));

        Assert.Equal(key, Typed(doc, "k"));
    }

    [Fact]
    public void A_semantic_search_function_is_unknown_with_its_reason()
    {
        var doc = Procedure("SELECT s.keyphrase FROM SEMANTICKEYPHRASETABLE(dbo.entry, definition) AS s;");

        Assert.StartsWith("unknown: unsupported-source", Typed(doc, "keyphrase"), StringComparison.Ordinal);
        Assert.Contains("SEMANTICKEYPHRASETABLE is a semantic search function", Why(doc, "keyphrase"), StringComparison.Ordinal);
    }

    // ---- reading the full-text index ------------------------------------------------------------------------------------

    [Fact]
    public void A_folder_records_each_full_text_index_s_key_on_its_table()
    {
        var model = SqlScriptFolder.Read(Path.Combine(RepositoryPaths.Fixtures, "glossary", "database")).Model;

        Assert.Equal(["pk_entry"], model.FindTable(new ObjectName("lex", "entry"))!.FullTextKeys.Select(k => k.Index));
        Assert.Equal(["ux_note_code"], model.FindTable(new ObjectName("lex", "note"))!.FullTextKeys.Select(k => k.Index));
        Assert.Equal(["uq_label_ref"], model.FindTable(new ObjectName("lex", "label"))!.FullTextKeys.Select(k => k.Index));
    }
}
