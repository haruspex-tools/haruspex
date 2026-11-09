using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests;

/// <summary>
/// Semantics' reading of the schema: what types, alias types and CHECK constraints guarantee about a column's
/// values. Each form that is read has a test, and so does each form that must be ignored, because ignoring is what
/// keeps a constraint the reader doesn't understand from narrowing a type wrongly.
/// </summary>
public class SchemaFactsTests
{
    private static DatabaseModel Model(string sql)
    {
        using var dir = new TempDirectory();
        dir.File("schema.sql", sql);
        var read = SqlScriptFolder.Read(dir.Path);
        Assert.Empty(read.Diagnostics);
        return read.Model;
    }

    /// <summary>The facts about column <paramref name="column"/> of <c>dbo.t</c>.</summary>
    private static ColumnFacts Facts(string sql, string column = "c")
    {
        var model = Model(sql);
        var table = model.FindTable(new ObjectName("dbo", "t"))!;
        return SchemaRules.ColumnFacts(model, table, table.Column(column)!);
    }

    /// <summary>The facts about <c>c</c>, declared as <paramref name="definition"/>, with each check in <paramref name="checks"/> on the table.</summary>
    private static ColumnFacts Column(string definition, params string[] checks) =>
        Facts($"CREATE TABLE dbo.t (id int NOT NULL PRIMARY KEY, c {definition}{string.Concat(checks.Select(k => ", " + k))});");

    private static bool NeverNull(string sql, string column = "c")
    {
        var model = Model(sql);
        var table = model.FindTable(new ObjectName("dbo", "t"))!;
        return SchemaRules.IsNeverNull(model, table, table.Column(column)!);
    }

    private static string Bound(BoundFact? bound) => bound is null ? "none" : (bound.Inclusive ? "" : "excl ") + bound.Value;

    private static string Source(FactSource? from) => from switch
    {
        TypeSource t => "type " + t.Type,
        CheckSource c => "check " + c.Check,
        null => "none",
        _ => throw new InvalidOperationException(),
    };

    // ---- types --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("tinyint NULL", "0", "255")]
    [InlineData("smallint NULL", "-32768", "32767")]
    [InlineData("int NULL", "-2147483648", "2147483647")]
    [InlineData("bigint NULL", "-9223372036854775808", "9223372036854775807")]
    [InlineData("decimal(19, 2) NULL", "-99999999999999999.99", "99999999999999999.99")]
    [InlineData("numeric(5, 5) NULL", "-0.99999", "0.99999")]
    [InlineData("decimal NULL", "-999999999999999999", "999999999999999999")]
    [InlineData("decimal(38) NULL", "-99999999999999999999999999999999999999", "99999999999999999999999999999999999999")]
    [InlineData("money NULL", "-922337203685477.5808", "922337203685477.5807")]
    [InlineData("smallmoney NULL", "-214748.3648", "214748.3647")]
    public void An_exact_numeric_type_bounds_its_values(string definition, string min, string max)
    {
        var facts = Column(definition);

        Assert.Equal(min, Bound(facts.Min));
        Assert.Equal(max, Bound(facts.Max));
        Assert.StartsWith("type ", Source(facts.Min?.From), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("decimal(19, 2) NULL", 19, 2)]
    [InlineData("decimal NULL", 18, 0)]
    [InlineData("numeric(9) NULL", 9, 0)]
    [InlineData("money NULL", 19, 4)]
    [InlineData("smallmoney NULL", 10, 4)]
    public void A_decimal_type_has_its_digits(string definition, int precision, int scale)
    {
        var digits = Column(definition).Digits!;

        Assert.Equal((precision, scale), (digits.Precision, digits.Scale));
    }

    [Theory]
    [InlineData("float NULL")]
    [InlineData("real NULL")]
    [InlineData("bit NULL")]
    [InlineData("date NULL")]
    [InlineData("nvarchar(max) NULL")]
    [InlineData("varchar(max) NULL")]
    public void Other_types_claim_no_facets(string definition)
    {
        var facts = Column(definition);

        Assert.NotNull(facts.Type);
        Assert.Equal(new ColumnFacts(facts.Type), facts);
    }

    [Theory]
    [InlineData("nvarchar(40) NULL", 40, false, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("nchar(5) NULL", 5, true, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("nchar NULL", 1, true, TextUnit.Utf16, TextEncoding.Unknown)]
    [InlineData("varchar(5) NULL", 5, false, TextUnit.Bytes, TextEncoding.Unknown)]
    [InlineData("char(5) NULL", 5, true, TextUnit.Bytes, TextEncoding.Unknown)]
    [InlineData("char(5) COLLATE Latin1_General_100_CI_AS NULL", 5, true, TextUnit.Bytes, TextEncoding.SingleByte)]
    [InlineData("varchar(5) COLLATE SQL_Latin1_General_CP1_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.SingleByte)]
    [InlineData("varchar(5) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL", 5, false, TextUnit.Bytes, TextEncoding.Utf8)]
    [InlineData("varchar(5) COLLATE Japanese_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    [InlineData("varchar(5) COLLATE Chinese_PRC_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    [InlineData("varchar(5) COLLATE Korean_Wansung_CI_AS NULL", 5, false, TextUnit.Bytes, TextEncoding.DoubleByte)]
    // A Unicode type counts UTF-16 code units whatever its collation.
    [InlineData("nvarchar(5) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL", 5, false, TextUnit.Utf16, TextEncoding.Unknown)]
    public void A_string_type_has_its_length_and_what_it_counts(string definition, int length, bool isFixed, TextUnit unit, TextEncoding encoding)
    {
        var fact = Column(definition).Length!;

        Assert.Equal((length, isFixed, unit, encoding), (fact.Value, fact.IsFixed, fact.Unit, fact.Encoding));
    }

    [Fact]
    public void A_computed_column_claims_nothing() =>
        Assert.Null(Facts("CREATE TABLE dbo.t (a int NOT NULL, c AS a * 2);").Type);

    // ---- alias types --------------------------------------------------------------------------------

    private const string Code = "CREATE TYPE dbo.code FROM nvarchar(10) NOT NULL;\nGO\n";

    [Fact]
    public void An_alias_type_resolves_with_its_facets_attributed_to_the_alias()
    {
        var facts = Facts(Code + "CREATE TABLE dbo.t (c dbo.code);");

        Assert.Equal("nvarchar(10)", facts.Type!.BuiltIn.ToString());
        Assert.Equal(10, facts.Length!.Value);
        Assert.Equal("type dbo.code", Source(facts.Length.From));
    }

    [Fact]
    public void An_unqualified_alias_type_resolves_when_one_schema_defines_it() =>
        Assert.Equal("nvarchar(10)", Facts(Code + "CREATE TABLE dbo.t (c code);").Type!.BuiltIn.ToString());

    [Fact]
    public void An_unqualified_alias_type_that_two_schemas_define_is_not_resolved() =>
        Assert.Null(Facts(Code + "CREATE TYPE other.code FROM int;\nGO\nCREATE TABLE dbo.t (c code);").Type);

    [Fact]
    public void An_unqualified_name_shared_with_a_clr_type_is_not_resolved() =>
        Assert.Null(Facts(Code + "CREATE TYPE other.code EXTERNAL NAME lib.[Code];\nGO\nCREATE TABLE dbo.t (c code);").Type);

    [Fact]
    public void An_alias_type_that_is_not_defined_is_not_resolved() =>
        Assert.Null(Facts("CREATE TABLE dbo.t (c dbo.nowhere);").Type);

    [Fact]
    public void Sysname_is_nvarchar_128()
    {
        var facts = Facts("CREATE TABLE dbo.t (c sysname);");

        Assert.Equal("nvarchar(128)", facts.Type!.BuiltIn.ToString());
        Assert.Equal("type sysname", Source(facts.Length!.From));
    }

    [Theory]
    [InlineData(Code + "CREATE TABLE dbo.t (c dbo.code);", true)]
    [InlineData(Code + "CREATE TABLE dbo.t (c dbo.code NULL);", false)]
    [InlineData("CREATE TYPE dbo.code FROM nvarchar(10) NULL;\nGO\nCREATE TABLE dbo.t (c dbo.code);", false)]
    [InlineData("CREATE TYPE dbo.code FROM nvarchar(10);\nGO\nCREATE TABLE dbo.t (c dbo.code);", false)]
    [InlineData("CREATE TABLE dbo.t (c sysname);", true)]
    [InlineData("CREATE TABLE dbo.t (c sysname NULL);", false)]
    public void A_column_that_declares_no_nullability_takes_its_alias_types(string sql, bool neverNull) =>
        Assert.Equal(neverNull, NeverNull(sql));

    // ---- check constraints: what is read ------------------------------------------------------------

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
    [InlineData("int NULL CHECK (c >= -10 AND c < 100)", "-10", "excl 100")]
    [InlineData("int NULL CHECK (c > 0)", "excl 0", "2147483647")]
    [InlineData("int NULL CHECK (0 < c)", "excl 0", "2147483647")]
    [InlineData("int NULL CHECK (100 >= c)", "-2147483648", "100")]
    [InlineData("decimal(9, 4) NULL CHECK (c > 0 AND c < 1)", "excl 0", "excl 1")]
    [InlineData("decimal(5, 2) NULL CHECK (c >= 0.5)", "0.5", "999.99")]
    // The type's bound when it is tighter than the constraint's.
    [InlineData("tinyint NULL CHECK (c > -5 AND c <= 300)", "0", "255")]
    // The tighter of two constraints.
    [InlineData("int NULL CHECK (c >= 0), CHECK (c >= 5), CHECK (c <= 10)", "5", "10")]
    // At the same value an exclusive bound is tighter.
    [InlineData("int NULL CHECK (c >= 5 AND c > 5)", "excl 5", "2147483647")]
    // A conjunct that isn't understood is skipped; the rest still holds.
    [InlineData("int NULL CHECK (c > 0 AND c % 2 = 0)", "excl 0", "2147483647")]
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

    [Fact]
    public void A_table_constraint_on_one_column_is_read()
    {
        var facts = Column("int NULL", "CONSTRAINT ck_t CHECK (c BETWEEN 1 AND 5)");

        Assert.Equal("1", Bound(facts.Min));
        Assert.Equal("check ck_t", Source(facts.Min!.From));
    }

    // ---- check constraints: what is ignored, and why that is safe -----------------------------------

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
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_c;", "excl 0")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ALL;\nGO\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ALL;", "excl 0")]
    [InlineData("ALTER TABLE dbo.t NOCHECK CONSTRAINT ck_c;\nALTER TABLE dbo.t WITH CHECK CHECK CONSTRAINT ck_d, ck_c;", "excl 0")]
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

    [Fact]
    public void Nocheck_of_another_constraint_leaves_this_one_read() =>
        Assert.Equal("excl 0", Bound(Facts("CREATE TABLE dbo.t (c int NULL CONSTRAINT ck_c CHECK (c > 0), d int NULL CONSTRAINT ck_d CHECK (d > 0));\nGO\nALTER TABLE dbo.t NOCHECK CONSTRAINT ck_d;").Min));

    [Fact]
    public void A_constraint_on_another_column_says_nothing_about_this_one() =>
        Assert.Equal("-2147483648", Bound(Facts("CREATE TABLE dbo.t (c int NULL, d int NULL CHECK (d > 0));").Min));

    [Theory]
    [InlineData("int NULL CHECK (c IN (1, 2))")]
    [InlineData("varchar(10) NULL CHECK (c IN ('a'))")]
    [InlineData("int NULL CHECK (c IS NOT NULL)")]
    public void A_constraint_never_makes_a_column_non_null(string definition) =>
        Assert.False(NeverNull($"CREATE TABLE dbo.t (c {definition});"));
}
