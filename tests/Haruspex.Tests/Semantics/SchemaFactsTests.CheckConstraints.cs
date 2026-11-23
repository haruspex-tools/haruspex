using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Semantics;

public partial class SchemaFactsTests
{
    [Fact]
    public void In_gives_a_set_of_strings_that_match_under_the_collation()
    {
        var values = Column("varchar(10) NULL CONSTRAINT ck_c CHECK (c IN ('open', 'held', 'closed'))").Values!;

        Assert.Equal(["open", "held", "closed"], values.Strings);
        Assert.Null(values.Numbers);
        Assert.Equal(ValueComparison.Collation, values.Comparison);
        Assert.Equal("check ck_c", Source(Assert.Single(values.From)));
    }

    [Theory]
    // A binary collation compares exactly but for trailing spaces.
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN ('open', 'a.b'))", ValueComparison.Padded)]
    [InlineData("char(4) COLLATE Latin1_General_BIN NULL CHECK (c = 'NE')", ValueComparison.Padded)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2_UTF8 NULL CHECK (c IN (N'é'))", ValueComparison.Padded)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN (N'é', N'ŝ'))", ValueComparison.Padded)]
    // A code-page column of a collation whose fold is proven: a value maps to the one string it folds like.
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c IN ('open'))", ValueComparison.Canonical)]
    [InlineData("char(6) COLLATE SQL_Latin1_General_CP1_CI_AS NULL CHECK (c IN ('open', 'on-hold'))", ValueComparison.Canonical)]
    // Otherwise case, accents and more may differ, and how isn't proven: no collation known, Unicode (whose comparison looks at a
    // character's neighbours), a collation not proven, a string that isn't plain, or two strings that fold alike.
    [InlineData("varchar(10) NULL CHECK (c IN ('open'))", ValueComparison.Collation)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c IN ('open'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL CHECK (c IN ('open'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE French_CI_AS NULL CHECK (c IN ('open'))", ValueComparison.Collation)]
    // A name no collation has: not read as binary, though it says BIN2.
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2_UFT8 NULL CHECK (c IN ('open'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS NULL CHECK (c IN ('open', 'café'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS NULL CHECK (c IN ('open', 'OPEN'))", ValueComparison.Collation)]
    // A literal outside ASCII in a code-page column could be converted to another character ('?') before comparing.
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN (N'é'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN ('a\\b'))", ValueComparison.Collation)]
    public void A_set_of_strings_is_exact_but_for_trailing_spaces_only_under_a_binary_collation(string definition, ValueComparison comparison) =>
        Assert.Equal(comparison, Column(definition).Values!.Comparison);

    [Theory]
    // An N'…' literal outranks varchar, so the column is converted to Unicode and compared by the collation's Unicode rules,
    // not the code-page rules the fold records (observed: A_national_literal_compares_a_code_page_column_by_unicode_rules).
    [InlineData("varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL CHECK (c IN (N'v1', N'v2'))", ValueComparison.Collation)]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS NULL CHECK (c IN ('open', N'shut'))", ValueComparison.Collation)]
    [InlineData("char(6) COLLATE Latin1_General_100_CS_AS NULL CHECK (c = N'open')", ValueComparison.Collation)]
    // Under a binary collation printable ASCII is the same code point either way, so the set stays exact but for spaces.
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN (N'v1', N'v2'))", ValueComparison.Padded)]
    // The same strings written without N fold canonically.
    [InlineData("varchar(10) COLLATE SQL_Latin1_General_CP1_CI_AS NULL CHECK (c IN ('v1', 'v2'))", ValueComparison.Canonical)]
    public void A_national_literal_against_a_code_page_column_gives_no_canonical_fold(string definition, ValueComparison comparison) =>
        Assert.Equal(comparison, Column(definition).Values!.Comparison);

    [Theory]
    // A literal without N is varchar in the database's code page, whatever the column is, so a character outside printable ASCII
    // may already be another ('Ω' becomes 'O') when it is compared (observed:
    // A_literal_without_n_is_converted_to_the_databases_code_page_before_it_is_compared).
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN ('Ω', 'ok'))", ValueComparison.Collation)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_BIN2_UTF8 NULL CHECK (c IN ('Ā', 'ok'))", ValueComparison.Collation)]
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN NULL CHECK (c = 'a\\b')", ValueComparison.Collation)]
    // Written with N, or in printable ASCII, the literal is what is compared.
    [InlineData("nvarchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN (N'Ω', 'ok'))", ValueComparison.Padded)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_BIN2_UTF8 NULL CHECK (c IN (N'Ā', 'ok'))", ValueComparison.Padded)]
    public void A_literal_without_n_outside_printable_ascii_is_not_read_as_written(string definition, ValueComparison comparison) =>
        Assert.Equal(comparison, Column(definition).Values!.Comparison);

    [Theory]
    // A COLLATE in the condition decides the comparison, not the column's collation (observed:
    // A_collate_in_a_check_decides_how_it_compares). So a set compared so stays a note: no exact, padded or canonical claim.
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c IN ('open' COLLATE Latin1_General_100_CI_AS, 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c COLLATE Latin1_General_100_CI_AS IN ('open', 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c = 'open' COLLATE Latin1_General_100_CI_AS OR c = 'shut')")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CI_AS NULL CHECK (c IN ('open' COLLATE Latin1_General_100_CS_AS, 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN ('open' COLLATE Latin1_General_100_CI_AS, 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK ((c) COLLATE Latin1_General_100_CI_AS IN ('open', 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN (('open') COLLATE Latin1_General_100_CI_AS, 'shut'))")]
    // A COLLATE inside parentheses: the outer expression has none, and the one within still decides.
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK ((c COLLATE Latin1_General_100_CI_AS) IN ('open', 'shut'))")]
    [InlineData("varchar(10) COLLATE Latin1_General_100_CS_AS NULL CHECK (c IN (('open' COLLATE Latin1_General_100_CI_AS), 'shut'))")]
    public void A_set_a_check_compares_under_a_collate_of_its_own_stays_a_note(string definition) =>
        Assert.Equal((ValueComparison.Collation, null), (Column(definition).Values!.Comparison, Column(definition).Values!.Fold));

    [Theory]
    // SQL Server pads the shorter side before comparing, so 'a' passes IN ('a '): an item's own trailing spaces
    // mean nothing, and are removed (duplicates with them).
    [InlineData("varchar(6) COLLATE Latin1_General_100_BIN2 NULL CHECK (c IN ('a ', 'b', 'b  '))", "a|b")]
    [InlineData("varchar(6) COLLATE Latin1_General_100_BIN2 NULL CHECK (c = 'a  ')", "a")]
    [InlineData("varchar(6) NULL CHECK (c IN ('a ', ' b'))", "a| b")]
    public void A_strings_trailing_spaces_are_removed_from_the_set(string definition, string items) =>
        Assert.Equal(items, string.Join("|", Column(definition).Values!.Strings!));

    [Theory]
    [InlineData("int NULL CHECK (c IN (3, 1, 2))", "1,2,3")]
    [InlineData("int NULL CHECK (c = 1 OR c = 2 OR (c = 4))", "1,2,4")]
    [InlineData("int NULL CHECK (c IN (1, 2) OR c = -3)", "-3,1,2")]
    [InlineData("int NULL CHECK (5 = c)", "5")]
    [InlineData("decimal(5, 2) NULL CHECK (c IN (1.5, 2.25, 3))", "1.5,2.25,3")]
    // Values the type can't hold are dropped: tinyint has no -1.
    [InlineData("tinyint NULL CHECK (c IN (-1, 0, 255))", "0,255")]
    // Two constraints: the set is their intersection, and a bound narrows it.
    [InlineData("int NULL CHECK (c IN (1, 2, 3)), CHECK (c IN (2, 3, 4))", "2,3")]
    [InlineData("int NULL CHECK (c IN (1, 2, 3) AND c > 1)", "2,3")]
    public void In_and_equalities_give_an_exact_set_of_numbers(string definition, string expected)
    {
        var values = Column(definition).Values!;

        Assert.Equal(expected, string.Join(",", values.Numbers!));
        Assert.Equal(ValueComparison.Exact, values.Comparison);
    }

    [Theory]
    [InlineData("int NULL CHECK (c BETWEEN 1 AND 5)", "1", "5")]
    [InlineData("int NULL CHECK (c >= -10 AND c < 100)", "-10", "exclusive 100")]
    [InlineData("int NULL CHECK (c > 0)", "exclusive 0", "2147483647")]
    [InlineData("int NULL CHECK (0 < c)", "exclusive 0", "2147483647")]
    [InlineData("int NULL CHECK (100 >= c)", "-2147483648", "100")]
    [InlineData("decimal(9, 4) NULL CHECK (c > 0 AND c < 1)", "exclusive 0", "exclusive 1")]
    [InlineData("decimal(5, 2) NULL CHECK (c >= 0.5)", "0.5", "999.99")]
    // The type's bound when it is tighter than the constraint's.
    [InlineData("tinyint NULL CHECK (c > -5 AND c <= 300)", "0", "255")]
    // The tighter of two constraints.
    [InlineData("int NULL CHECK (c >= 0), CHECK (c >= 5), CHECK (c <= 10)", "5", "10")]
    // At the same value an exclusive bound is tighter.
    [InlineData("int NULL CHECK (c >= 5 AND c > 5)", "exclusive 5", "2147483647")]
    // A conjunct that isn't understood is skipped; the rest still holds.
    [InlineData("int NULL CHECK (c > 0 AND c % 2 = 0)", "exclusive 0", "2147483647")]
    [InlineData("int NULL CHECK (c IS NOT NULL AND c <= 9)", "-2147483648", "9")]
    public void Between_and_comparisons_give_bounds(string definition, string min, string max)
    {
        var facts = Column(definition);

        Assert.Equal(min, Bound(facts.Min));
        Assert.Equal(max, Bound(facts.Max));
    }

    [Fact]
    public void A_bound_from_a_constraint_names_it()
    {
        var facts = Column("int NULL CONSTRAINT ck_c CHECK (c > 0)");

        Assert.Equal("check ck_c", Source(facts.Min!.From));
        Assert.Equal("type int", Source(facts.Max!.From));
    }

    [Theory]
    [InlineData("nvarchar(20) NULL CHECK (len(c) > 0)", 1)]
    [InlineData("nvarchar(20) NULL CHECK (LEN(c) >= 3)", 3)]
    [InlineData("nvarchar(20) NULL CHECK (len(c) > 2)", 3)]
    [InlineData("nvarchar(20) NULL CHECK (0 < len(c))", 1)]
    [InlineData("nvarchar(20) NULL CHECK (len(c) <> 0)", 1)]
    [InlineData("varchar(20) NULL CHECK (c <> '')", 1)]
    [InlineData("nvarchar(20) NULL CHECK (c != N'')", 1)]
    [InlineData("nvarchar(20) NULL CHECK (len(c) > 0 AND len(c) >= 4)", 4)]
    public void Len_and_not_empty_give_a_minimum_length(string definition, int min) =>
        Assert.Equal(min, Column(definition).MinLength!.Value);

    [Theory]
    // LEN counts a surrogate pair as one character, so its minimum is in code points, only where it was observed to
    // (Len_counts_a_surrogate_pair_as_one_only_under_a_supplementary_character_collation): of nvarchar, under _SC or a version
    // 140 collation that isn't binary; of varchar, under _UTF8, binary too. Anything else, an unknown collation or a COLLATE in
    // the condition keeps UTF-16 code units, which hold under either.
    [InlineData("nvarchar(20) COLLATE Latin1_General_100_CI_AS_SC NULL CHECK (LEN(c) >= 3)", true)]
    [InlineData("nvarchar(20) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL CHECK (LEN(c) >= 3)", true)]
    [InlineData("nvarchar(20) COLLATE Japanese_XJIS_140_CI_AS NULL CHECK (LEN(c) >= 3)", true)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL CHECK (LEN(c) >= 3)", true)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_BIN2_UTF8 NULL CHECK (LEN(c) >= 3)", true)]
    [InlineData("nvarchar(20) COLLATE Latin1_General_100_BIN2_UTF8 NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("nvarchar(20) COLLATE Japanese_XJIS_140_BIN NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("nvarchar(20) COLLATE Japanese_XJIS_140_BIN2 NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("varchar(20) COLLATE Latin1_General_100_CI_AS_SC NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("varchar(20) COLLATE Japanese_XJIS_140_CI_AS NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("nvarchar(20) COLLATE Latin1_General_100_CI_AS NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("nvarchar(20) NULL CHECK (LEN(c) >= 3)", false)]
    [InlineData("nvarchar(20) COLLATE Latin1_General_100_CI_AS_SC NULL CHECK (LEN(c COLLATE Latin1_General_100_CI_AS) >= 3)", false)]
    public void A_len_minimum_is_in_code_points_only_under_a_supplementary_character_collation(string definition, bool codePoints) =>
        Assert.Equal((3, codePoints), (Column(definition).MinLength!.Value, Column(definition).MinLength!.InCodePoints));

    [Fact]
    public void A_table_constraint_on_one_column_is_read()
    {
        var facts = Column("int NULL", "CONSTRAINT ck_t CHECK (c BETWEEN 1 AND 5)");

        Assert.Equal("1", Bound(facts.Min));
        Assert.Equal("check ck_t", Source(facts.Min!.From));
    }

    [Theory]
    // More than one column: not a single-column constraint.
    [InlineData("int NULL", "d int NULL", "CHECK (c > 0 AND d > 0)")]
    [InlineData("int NULL", "d int NULL", "CHECK (c < d)")]
    // Forms that are not read.
    [InlineData("int NULL CHECK (c NOT IN (1, 2))", null, null)]
    [InlineData("int NULL CHECK (c NOT BETWEEN 1 AND 5)", null, null)]
    [InlineData("int NULL CHECK (NOT (c > 5))", null, null)]
    [InlineData("int NULL CHECK (c <> 0)", null, null)]
    [InlineData("int NULL CHECK (c % 2 = 0)", null, null)]
    // A NULL among the values makes IN UNKNOWN for any value outside them, which passes.
    [InlineData("int NULL CHECK (c IN (1, NULL))", null, null)]
    // OR with anything but another value set: either side could be the TRUE one.
    [InlineData("int NULL CHECK (c = 1 OR c > 10)", null, null)]
    [InlineData("int NULL CHECK (c = 1 OR c IS NULL)", null, null)]
    // A literal that doesn't fit the column's precision and scale, or of another type: not read, rather than
    // modelling how SQL Server converts the two to a common type.
    [InlineData("money NULL CHECK (c >= 1.00004)", null, null)]
    [InlineData("int NULL CHECK (c > 1.5)", null, null)]
    [InlineData("int NULL CHECK (c IN ('1', '2'))", null, null)]
    [InlineData("int NULL CHECK (c > 1e3)", null, null)]
    [InlineData("int NULL CHECK (c > $5)", null, null)]
    [InlineData("decimal(5, 2) NULL CHECK (c < 100000)", null, null)]
    // Floating point: the literal is rounded to the column's binary type first.
    [InlineData("float NULL CHECK (c > 0 AND c IN (1, 2))", null, null)]
    [InlineData("real NULL CHECK (c BETWEEN 0 AND 1)", null, null)]
    // Strings have no bounds here, and only '' has a meaning for <>.
    [InlineData("varchar(10) NULL CHECK (c >= 'a')", null, null)]
    [InlineData("varchar(10) NULL CHECK (c <> 'x')", null, null)]
    [InlineData("varchar(10) NULL CHECK (len(c) < 5)", null, null)]
    [InlineData("varchar(10) NULL CHECK (c LIKE '[a-z]%')", null, null)]
    // Other types.
    [InlineData("date NULL CHECK (c > '2000-01-01')", null, null)]
    [InlineData("bit NULL CHECK (c = 1)", null, null)]
    public void A_constraint_that_is_not_understood_proves_nothing(string definition, string? other, string? tableCheck)
    {
        var columns = other is null ? definition : definition + ", " + other;
        var facts = tableCheck is null ? Column(columns) : Column(columns, tableCheck);

        Assert.Equal(SchemaRules.TypeFacts(facts.Type!, null), facts);
    }

    [Theory]
    [InlineData("CONSTRAINT ck_c CHECK NOT FOR REPLICATION (c > 0)", "")]
    [InlineData("CONSTRAINT ck_c CHECK (c > 0)", "ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;")]
    [InlineData("CONSTRAINT ck_c CHECK (c > 0)", "ALTER TABLE dbo.t NOCHECK CONSTRAINT ALL;")]
    public void A_constraint_that_may_not_check_every_row_proves_nothing(string check, string alter)
    {
        var facts = Facts($"CREATE TABLE dbo.t (c int NULL {check});\nGO\n{alter}");

        Assert.Equal("-2147483648", Bound(facts.Min));
    }

    /// <summary>The facts about <c>c</c> of <c>dbo.t</c>, from several script files.</summary>
    private static ColumnFacts FactsFrom(params (string File, string Sql)[] files)
    {
        using var dir = new TempDirectory();
        foreach (var (file, sql) in files)
            dir.File(file, sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        var table = read.Model.FindTable(new ObjectName("dbo", "t"))!;
        return SchemaRules.ColumnFacts(read.Model, table, table.Column("c")!);
    }

    private const string Checked = "CREATE TABLE dbo.t (c int NULL CONSTRAINT ck_c CHECK (c > 0), d int NULL CONSTRAINT ck_d CHECK (d > 0));\nGO\n";

    [Theory]
    // Re-enabled and every row validated, later in the same script: trusted again.
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;", "exclusive 0")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ALL;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ALL;", "exclusive 0")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_d, ck_c;", "exclusive 0")]
    // Re-enabled without validating the rows written meanwhile (is_not_trusted stays 1): still nothing.
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t CHECK CONSTRAINT ck_c;", "-2147483648")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t WITH NOCHECK CHECK CONSTRAINT ck_c;", "-2147483648")]
    // Validated before it was disabled, or another constraint validated: still disabled.
    [InlineData("ALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;", "-2147483648")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_d;", "-2147483648")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ALL;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_d;", "-2147483648")]
    public void A_disabled_constraint_is_trusted_again_only_after_with_check_check_later_in_the_same_script(string alter, string min) =>
        Assert.Equal(min, Bound(Facts(Checked + alter).Min));

    [Fact]
    public void Re_enabling_in_another_script_does_not_count_because_the_order_is_unknown() =>
        Assert.Equal("-2147483648", Bound(FactsFrom(
            ("a.sql", Checked + "ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;"),
            ("b.sql", "ALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;")).Min));

    [Theory]
    // Added after the table, WITH NOCHECK (existing rows are not validated) or not: not read either way.
    [InlineData("ALTER TABLE dbo.t WITH NOCHECK ADD CONSTRAINT ck_late CHECK (c > 0);")]
    [InlineData("ALTER TABLE dbo.t ADD CONSTRAINT ck_late CHECK (c > 0);")]
    public void A_constraint_added_by_alter_table_is_not_read(string alter) =>
        Assert.Equal("-2147483648", Bound(Facts("CREATE TABLE dbo.t (c int NULL);\nGO\n" + alter).Min));

    [Theory]
    // A NOCHECK anywhere it could run disables the constraint: under IF, in a block, in TRY/CATCH, in a loop, and in
    // a procedure, trigger or function body (the bulk-load pattern). Whether it runs isn't knowable, so it might.
    [InlineData("IF 1 = 1 ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;")]
    [InlineData("BEGIN ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c; END")]
    [InlineData("BEGIN TRY ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c; END TRY BEGIN CATCH END CATCH")]
    [InlineData("WHILE 1 = 0 ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;")]
    [InlineData("CREATE PROCEDURE dbo.load AS BEGIN ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c; INSERT dbo.t (c) VALUES (-7); ALTER TABLE dbo.t CHECK CONSTRAINT ck_c; END")]
    [InlineData("CREATE TRIGGER dbo.tr ON dbo.t AFTER INSERT AS ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;")]
    // A WITH CHECK CHECK inside a body may never run, so it restores nothing.
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nCREATE PROCEDURE dbo.fix AS ALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nIF 1 = 1 ALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;")]
    public void A_nocheck_at_any_depth_disables_the_constraint(string script) =>
        Assert.Equal("-2147483648", Bound(Facts(Checked + script).Min));

    [Theory]
    // A bulk load that doesn't check constraints writes rows no check has seen, and leaves each check of its table not trusted
    // (observed: A_bulk_load_without_check_constraints_writes_rows_no_check_has_seen), wherever it is, as NOCHECK ALL does.
    [InlineData("BULK INSERT [dbo].[t] FROM '/data/t.csv';")]
    [InlineData("CREATE PROCEDURE dbo.load AS BULK INSERT dbo.t FROM '/data/t.csv';")]
    [InlineData("IF 1 = 0 BULK INSERT t FROM '/data/t.csv';")]
    [InlineData("INSERT BULK dbo.t (c int, d int);")]
    [InlineData("CREATE PROCEDURE dbo.load AS INSERT INTO dbo.t WITH (IGNORE_CONSTRAINTS) (c) SELECT BulkColumn FROM OPENROWSET(BULK '/data/t.txt', SINGLE_CLOB) AS f;")]
    public void A_bulk_load_without_check_constraints_disables_every_check_of_its_table(string load)
    {
        Assert.Equal("-2147483648", Bound(Facts(Checked + load).Min));
        Assert.Equal("-2147483648", Bound(Facts(Checked + load, "d").Min));
    }

    [Theory]
    // With CHECK_CONSTRAINTS each row is checked, and an OPENROWSET(BULK) insert without IGNORE_CONSTRAINTS is checked as any
    // insert is (observed: error 547 for each); a WITH CHECK CHECK later in the same script validates what a load wrote.
    [InlineData("BULK INSERT dbo.t FROM '/data/t.csv' WITH (CHECK_CONSTRAINTS);")]
    [InlineData("CREATE PROCEDURE dbo.load AS INSERT BULK dbo.t (c int, d int) WITH (CHECK_CONSTRAINTS);")]
    [InlineData("CREATE PROCEDURE dbo.load AS INSERT INTO dbo.t (c) SELECT BulkColumn FROM OPENROWSET(BULK '/data/t.txt', SINGLE_CLOB) AS f;")]
    [InlineData("BULK INSERT dbo.t FROM '/data/t.csv';\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ALL;")]
    public void A_bulk_load_that_checks_constraints_leaves_them_trusted(string load) =>
        Assert.Equal("exclusive 0", Bound(Facts(Checked + load).Min));

    [Fact]
    public void An_unqualified_nocheck_disables_the_constraint_of_a_table_of_that_name_in_any_schema() =>
        Assert.Equal("-2147483648", Bound(Facts(
            "CREATE TABLE prb.t (c int NULL CONSTRAINT ck_c CHECK (c > 0));\nGO\nALTER TABLE t NOCHECK CONSTRAINT ck_c;", schema: "prb").Min));

    [Fact]
    public void An_unqualified_with_check_check_does_not_restore_because_which_table_it_means_is_not_known() =>
        Assert.Equal("-2147483648", Bound(Facts(
            "CREATE TABLE prb.t (c int NULL CONSTRAINT ck_c CHECK (c > 0));\nGO\nALTER TABLE prb.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE t WITH CHECK CHECK CONSTRAINT ck_c;", schema: "prb").Min));

    [Fact]
    public void A_nocheck_of_a_name_the_table_does_not_declare_may_mean_an_unnamed_check()
    {
        // An unnamed check gets a system-generated name, which a script copied from a live database may use.
        const string sql = "CREATE TABLE dbo.t (c int NULL CHECK (c > 0), d int NULL CONSTRAINT ck_d CHECK (d > 0));\nGO\nALTER TABLE dbo.t NOCHECK CONSTRAINT CK__t__c__3A81B327;";

        Assert.Equal("-2147483648", Bound(Facts(sql).Min));
        Assert.Equal("exclusive 0", Bound(Facts(sql, "d").Min));
    }

    [Fact]
    public void Nocheck_of_another_constraint_leaves_this_one_read() =>
        Assert.Equal("exclusive 0", Bound(Facts("CREATE TABLE dbo.t (c int NULL CONSTRAINT ck_c CHECK (c > 0), d int NULL CONSTRAINT ck_d CHECK (d > 0));\nGO\nALTER TABLE dbo.t NOCHECK CONSTRAINT ck_d;").Min));

    [Fact]
    public void A_constraint_on_another_column_says_nothing_about_this_one() =>
        Assert.Equal("-2147483648", Bound(Facts("CREATE TABLE dbo.t (c int NULL, d int NULL CHECK (d > 0));").Min));

    [Theory]
    [InlineData("int NULL CHECK (c IN (1, 2))")]
    [InlineData("varchar(10) NULL CHECK (c IN ('a'))")]
    [InlineData("int NULL CHECK (c IS NOT NULL)")]
    public void A_constraint_never_makes_a_column_non_null(string definition) =>
        Assert.False(NeverNull($"CREATE TABLE dbo.t (c {definition});"));

    [Theory]
    // Two columns whose names differ only in case, as a database of a case-sensitive collation allows: the CHECK names one, and
    // ignoring case it would be read for both. Neither is narrowed past its type (decision 0107); a CHECK on another column still is.
    [InlineData("qty")]
    [InlineData("Qty")]
    public void A_check_on_one_of_two_columns_whose_names_differ_only_in_case_proves_nothing_of_either(string column)
    {
        var model = Model("CREATE TABLE dbo.t (id int NOT NULL CHECK (id > 0), qty int NULL CHECK (qty BETWEEN 1 AND 5), Qty int NOT NULL);");
        var table = model.FindTable(new ObjectName("dbo", "t"))!;
        var facts = SchemaRules.ColumnFacts(model, table, table.Columns.Single(c => c.Name == column));

        Assert.Equal(("-2147483648", "2147483647"), (Bound(facts.Min), Bound(facts.Max)));
        Assert.Equal("0", Bound(SchemaRules.ColumnFacts(model, table, table.Column("id")!).Min).Replace("exclusive ", "", StringComparison.Ordinal));
    }
}
