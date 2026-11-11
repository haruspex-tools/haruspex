using Haruspex.Cli;
using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The common expressions (slice 6f): literals, <c>COALESCE</c>, <c>ISNULL</c>, <c>CAST</c>/<c>CONVERT</c> and their <c>TRY_</c>
/// forms, <c>CONCAT</c>, <c>CONCAT_WS</c>, string <c>+</c>, decimal arithmetic and <c>SUM</c>. Each type and each reason to be
/// null rests on an observation in the differential tests' <c>ExpressionObservationTests</c>, named where it matters.
/// </summary>
public class ExpressionTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, short varchar(5) NULL, label nvarchar(10) NOT NULL, amount decimal(10,2) NULL,
            total decimal(19,4) NOT NULL, n int NULL, small smallint NOT NULL, tiny tinyint NULL, big bigint NULL,
            num numeric(10,2) NULL, code char(4) NULL, born date NOT NULL, at datetime2(3) NULL, ratio float NULL,
            status varchar(10) NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NULL);
        GO
        """;

    private static IrDocument Procedure(string body)
    {
        using var dir = new TempDirectory();
        dir.File("tables.sql", Tables);
        dir.File("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}");
        var project = SqlProject.Load(dir.Path);
        Assert.Empty(project.Diagnostics);
        return project.Infer(project.Select([ObjectPattern.Parse("dbo.p")]).Procedures);
    }

    private static IReadOnlyList<IrField> Fields(IrDocument doc)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        var row = (IrRef)((IrArray)Assert.Single(results).Type).Items;
        return doc.Types[row.Name].Fields;
    }

    private static IrField Field(IrDocument doc, string name) => Assert.Single(Fields(doc), f => f.Name == name);

    private static string[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    /// <summary>The field's SQL type, or "unknown: reason" when it is not typed.</summary>
    private static string Sql(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => scalar.Sql ?? "(none)",
        _ => "unknown: " + string.Join("; ", doc.Unknowns.Where(u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Select(u => u.Reason)),
    };

    /// <summary>The type and the reasons to be null, as one string: <c>decimal(10,2) nullable-column</c>.</summary>
    private static string Typed(IrDocument doc, string name) => string.Join(" ", [Sql(doc, name), .. Rules(doc, name)]);

    // ---- literals ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Literals_are_of_their_own_length_and_digits_and_never_null()
    {
        // A_literals_type_is_its_own_digits_or_characters.
        var doc = Procedure("SELECT 'abc' AS s, N'ab' AS w, '' AS e, 2.5 AS d, 007.10 AS z, 2147483648 AS big, 1 AS i, -2147483648 AS least, -(2147483648) AS paren, -2147483649 AS below;");
        Assert.Equal("int", Typed(doc, "least"));
        Assert.Equal("int", Typed(doc, "paren"));
        Assert.Equal("numeric(10,0)", Typed(doc, "below"));

        // Negated again it is numeric(38,0) on the server, which is not modelled (review round 2).
        var twice = Procedure("SELECT -(-2147483648) AS a, - -2147483648 AS b, -(-5) AS c;");
        Assert.StartsWith("unknown", Sql(twice, "a"), StringComparison.Ordinal);
        Assert.StartsWith("unknown", Sql(twice, "b"), StringComparison.Ordinal);
        Assert.Equal("varchar(3)", Typed(doc, "s"));
        Assert.Equal("nvarchar(2)", Typed(doc, "w"));
        Assert.Equal("varchar(1)", Typed(doc, "e"));
        Assert.Equal("numeric(2,1)", Typed(doc, "d"));
        Assert.Equal("numeric(3,2)", Typed(doc, "z"));
        Assert.Equal("numeric(10,0)", Typed(doc, "big"));
        Assert.Equal("int", Typed(doc, "i"));
    }

    [Fact]
    public void A_varchar_literal_beyond_ascii_is_not_typed()
    {
        // Its length in bytes depends on the database's code page.
        var doc = Procedure("SELECT 'café' AS s;");
        Assert.StartsWith("unknown", Sql(doc, "s"), StringComparison.Ordinal);
    }

    // ---- COALESCE and ISNULL -----------------------------------------------------------------------------------------

    [Fact]
    public void Coalesce_is_of_the_highest_precedence_type_and_never_null_when_an_argument_is_not()
    {
        // Coalesce_takes_the_type_of_highest_precedence_and_isnull_the_first_arguments.
        var doc = Procedure("""
            SELECT COALESCE(short, 'a longer literal') AS a, COALESCE(amount, 0) AS b, COALESCE(amount, n) AS c, COALESCE(num, amount) AS d,
                   COALESCE(short, label) AS e, COALESCE(at, born) AS f, COALESCE(n, big) AS g, COALESCE(short, NULL) AS h, COALESCE(amount, 123456789) AS i,
                   COALESCE(code, short) AS j, COALESCE(amount, small) AS k
            FROM dbo.t;
            """);
        Assert.Equal("varchar(16)", Typed(doc, "a"));
        Assert.Equal("decimal(10,2)", Typed(doc, "b"));
        Assert.Equal("decimal(12,2) nullable-column nullable-column", Typed(doc, "c"));
        Assert.Equal("numeric(10,2) nullable-column nullable-column", Typed(doc, "d"));
        Assert.Equal("nvarchar(10)", Typed(doc, "e"));
        Assert.Equal("datetime2(3)", Typed(doc, "f"));
        Assert.Equal("bigint nullable-column nullable-column", Typed(doc, "g"));
        Assert.Equal("varchar(5) nullable-column", Typed(doc, "h"));
        Assert.Equal("decimal(11,2)", Typed(doc, "i"));
        Assert.Equal("varchar(5) nullable-column nullable-column", Typed(doc, "j"));
        Assert.Equal("decimal(10,2)", Typed(doc, "k"));
    }

    [Fact]
    public void Coalesce_of_a_string_and_a_number_is_the_number_and_may_overflow()
    {
        // The string is converted to the number: a failure is an error, an overflow NULL under the caller's options (slice 6k:
        // A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options).
        var doc = Procedure("SELECT COALESCE(short, 0) AS a FROM dbo.t;");
        Assert.Equal("int arithmetic-overflow", Typed(doc, "a"));
    }

    [Fact]
    public void Coalesce_after_a_subquery_can_still_be_null_but_isnull_cannot()
    {
        // Coalesce_reads_a_subquery_twice_so_it_can_be_null_where_isnull_cannot: the subquery read again may be NULL.
        var doc = Procedure("""
            SELECT COALESCE((SELECT MAX(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id), 0) AS c,
                   ISNULL((SELECT MAX(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id), 0) AS i,
                   COALESCE(t.n, (SELECT MAX(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id), 0) AS after
            FROM dbo.t AS t;
            """);
        Assert.Equal("int read-twice", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "i"));
        Assert.Equal("int read-twice", Typed(doc, "after"));
    }

    [Fact]
    public void Isnull_is_its_first_arguments_type_and_null_only_when_both_are_or_the_second_overflows()
    {
        // Isnull_converts_its_second_argument_to_the_firsts_type_cutting_a_string_short;
        // An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off (ISNULL(@ti, 1000)).
        var doc = Procedure("""
            SELECT ISNULL(short, 'a longer literal') AS a, ISNULL(tiny, 1000) AS b, ISNULL(tiny, 0) AS c, ISNULL(amount, 0) AS d, ISNULL(amount, n) AS e,
                   ISNULL(n, total) AS f, ISNULL(status, 'none') AS g, ISNULL(amount, 99999999.99) AS h, ISNULL(amount, 100000000) AS i, ISNULL(NULL, small) AS j
            FROM dbo.t;
            """);
        Assert.Equal("varchar(5)", Typed(doc, "a"));
        Assert.Equal("tinyint nullable-column arithmetic-overflow", Typed(doc, "b"));
        Assert.Equal("tinyint", Typed(doc, "c"));
        Assert.Equal("decimal(10,2)", Typed(doc, "d"));
        // An int may not fit decimal(10,2), which has 8 integer digits.
        Assert.Equal("decimal(10,2) nullable-column nullable-column arithmetic-overflow", Typed(doc, "e"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "f"));
        Assert.Equal("decimal(10,2)", Typed(doc, "h"));
        Assert.Equal("decimal(10,2) nullable-column arithmetic-overflow", Typed(doc, "i"));
        Assert.Equal("smallint", Typed(doc, "j"));

        // The second argument may be outside the first's CHECK set, so the set does not hold.
        var g = (IrScalar)Field(doc, "g").Type;
        Assert.Equal("varchar(10)", g.Sql);
        Assert.Null(g.Facets?.Values);
        Assert.Empty(Rules(doc, "g"));
    }

    [Fact]
    public void Isnull_of_a_column_never_null_is_the_column_with_its_facts()
    {
        var doc = Procedure("SELECT ISNULL(label, short) AS a FROM dbo.t WHERE status IS NOT NULL;");
        Assert.Equal("nvarchar(10)", Typed(doc, "a"));
    }

    // ---- SUM -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Sum_widens_its_argument_and_overflows_unless_a_decimal_of_19_digits_or_fewer()
    {
        // Sum_widens_its_argument; An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off.
        // A decimal of p ≤ 19 digits summed over fewer than 2^63 rows stays below 10^38, so it cannot overflow decimal(38, s).
        var doc = Procedure("SELECT SUM(amount) AS a, SUM(n) AS b, SUM(total) AS c, SUM(tiny) AS d, SUM(big) AS e, COALESCE(SUM(amount), 0) AS f, ISNULL(SUM(n), 0) AS g FROM dbo.t;");
        Assert.Equal("decimal(38,2) nullable-column empty-aggregate", Typed(doc, "a"));
        Assert.Equal("int nullable-column empty-aggregate arithmetic-overflow", Typed(doc, "b"));
        Assert.Equal("decimal(38,4) empty-aggregate", Typed(doc, "c"));
        Assert.Equal("int nullable-column empty-aggregate arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("bigint nullable-column empty-aggregate arithmetic-overflow", Typed(doc, "e"));
        Assert.Equal("decimal(38,2)", Typed(doc, "f"));
        Assert.Equal("int", Typed(doc, "g"));
    }

    [Fact]
    public void Sum_under_group_by_is_null_only_when_its_values_are()
    {
        var doc = Procedure("SELECT t_id, SUM(qty) AS q, SUM(id) AS i FROM dbo.u GROUP BY t_id;");
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "q"));
        Assert.Equal("int arithmetic-overflow", Typed(doc, "i"));
    }

    // ---- CAST and CONVERT ------------------------------------------------------------------------------------------

    [Fact]
    public void A_conversion_is_of_its_target_type_and_null_as_its_operand_unless_it_can_overflow()
    {
        // Cast_and_convert_are_of_the_target_type_whatever_the_style; A_conversion_that_cannot_overflow_keeps_every_value;
        // An_overflow_is_an_error_by_default_and_null_under_the_callers_ansi_warnings_and_arithabort_off.
        var doc = Procedure("""
            SELECT CONVERT(decimal(19,2), amount) AS a, CONVERT(decimal(19,2), total) AS b, CAST(total AS decimal(10,2)) AS c,
                   CONVERT(decimal(19,2), COALESCE(SUM(amount), 0)) AS d, CAST(MAX(id) AS varchar(11)) AS e, CAST(MAX(id) AS varchar(5)) AS f,
                   CONVERT(varchar(10), MAX(born), 23) AS g, CONVERT(varchar(30), MAX(at), 120) AS h, CAST(MAX(n) AS bigint) AS i,
                   CAST(MAX(label) AS int) AS j, CAST(MAX(born) AS datetime2(0)) AS k, CONVERT(decimal(19,2), MAX(n)) AS l, CAST(MAX(small) AS tinyint) AS m,
                   CAST(MAX(total) AS varchar(21)) AS o, CAST(MAX(total) AS varchar(20)) AS q, CAST(MAX(at) AS date) AS r, CAST(MAX(born) AS datetime) AS s,
                   CAST(MAX(label) AS nvarchar(4)) AS v, CAST(MAX(id) AS float) AS w, CAST(MAX(ratio) AS decimal(19,2)) AS x, CONVERT(varchar(30), MAX(total), 1) AS y
            FROM dbo.t;
            """);
        Assert.Equal("decimal(19,2) nullable-column", Typed(doc, "a"));
        Assert.Equal("decimal(19,2)", Typed(doc, "b"));
        Assert.Equal("decimal(10,2) arithmetic-overflow", Typed(doc, "c"));
        Assert.Equal("decimal(19,2) arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("varchar(11) empty-aggregate", Typed(doc, "e"));
        Assert.Equal("varchar(5) empty-aggregate arithmetic-overflow", Typed(doc, "f"));
        Assert.Equal("varchar(10) empty-aggregate", Typed(doc, "g"));
        Assert.Equal("varchar(30) nullable-column empty-aggregate", Typed(doc, "h"));
        Assert.Equal("bigint nullable-column empty-aggregate", Typed(doc, "i"));
        Assert.Equal("int empty-aggregate arithmetic-overflow", Typed(doc, "j"));
        Assert.Equal("datetime2(0) empty-aggregate", Typed(doc, "k"));
        Assert.Equal("decimal(19,2) nullable-column empty-aggregate", Typed(doc, "l"));
        Assert.Equal("tinyint empty-aggregate arithmetic-overflow", Typed(doc, "m"));
        Assert.Equal("varchar(21) empty-aggregate", Typed(doc, "o"));
        Assert.Equal("varchar(20) empty-aggregate arithmetic-overflow", Typed(doc, "q"));
        Assert.Equal("date nullable-column empty-aggregate", Typed(doc, "r"));
        Assert.Equal("datetime empty-aggregate arithmetic-overflow", Typed(doc, "s"));
        Assert.Equal("nvarchar(4) empty-aggregate", Typed(doc, "v"));
        Assert.Equal("float empty-aggregate", Typed(doc, "w"));
        Assert.Equal("decimal(19,2) nullable-column empty-aggregate arithmetic-overflow", Typed(doc, "x"));

        // A style on a number's string is not read: its length is not known, so it may overflow.
        Assert.Equal("varchar(30) empty-aggregate arithmetic-overflow", Typed(doc, "y"));
    }

    [Fact]
    public void A_conversion_to_its_own_type_keeps_the_value_with_its_facts()
    {
        var doc = Procedure("SELECT CAST(status AS varchar(10)) AS s FROM dbo.t;");
        Assert.NotNull(((IrScalar)Field(doc, "s").Type).Facets?.Values);
        Assert.Equal("varchar(10) nullable-column", Typed(doc, "s"));
    }

    [Fact]
    public void Try_cast_is_null_when_the_conversion_can_fail()
    {
        // Try_cast_and_try_convert_are_null_when_the_conversion_fails.
        var doc = Procedure("SELECT TRY_CAST(label AS int) AS a, TRY_CAST(id AS bigint) AS b, TRY_CONVERT(decimal(19,2), amount) AS c, TRY_CONVERT(date, label, 103) AS d FROM dbo.t;");
        Assert.Equal("int failed-conversion", Typed(doc, "a"));
        Assert.Equal("bigint", Typed(doc, "b"));
        Assert.Equal("decimal(19,2) nullable-column", Typed(doc, "c"));
        Assert.Equal("date failed-conversion", Typed(doc, "d"));
    }

    // ---- strings ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Concat_is_never_null_and_as_long_as_its_arguments()
    {
        // Concat_is_as_long_as_its_arguments_together_up_to_8000_bytes_and_never_null.
        var doc = Procedure("""
            SELECT CONCAT(short, label) AS a, CONCAT(short, ' ', code) AS b, CONCAT_WS('-', short, label) AS c, CONCAT(short, id) AS d,
                   CONCAT(short, NULL) AS e, CONCAT(label, amount) AS f, CONCAT_WS(N', ', short, code, NULL) AS g
            FROM dbo.t;
            """);
        Assert.Equal("nvarchar(15)", Typed(doc, "a"));
        Assert.Equal("varchar(10)", Typed(doc, "b"));
        Assert.Equal("nvarchar(16)", Typed(doc, "c"));
        Assert.Equal("varchar(17)", Typed(doc, "d"));
        Assert.Equal("varchar(5)", Typed(doc, "e"));
        Assert.StartsWith("unknown", Sql(doc, "f"), StringComparison.Ordinal);
        Assert.Equal("nvarchar(13)", Typed(doc, "g"));
    }

    [Fact]
    public void Concat_stops_at_8000_bytes_unless_an_argument_is_max()
    {
        var doc = Procedure("""
            DECLARE @long varchar(8000) = 'x', @wide nvarchar(4000) = N'y', @max nvarchar(max) = N'z';
            SELECT CONCAT(@long, 'a') AS a, CONCAT(@wide, 'a') AS b, CONCAT(@long, @wide) AS c, CONCAT(@max, 'a') AS d, @long + 'a' AS e;
            """);
        Assert.Equal("varchar(8000)", Typed(doc, "a"));
        Assert.Equal("nvarchar(4000)", Typed(doc, "b"));
        Assert.Equal("nvarchar(4000)", Typed(doc, "c"));
        Assert.Equal("nvarchar(max)", Typed(doc, "d"));
        Assert.Equal("varchar(8000)", Typed(doc, "e"));
    }

    [Fact]
    public void A_string_plus_is_null_when_an_operand_may_be()
    {
        // With CONCAT_NULL_YIELDS_NULL on, SQL Server's default, NULL + 'x' is NULL; off, it is 'x', which is less often
        // NULL (With_concat_null_yields_null_off_a_string_plus_null_is_the_string), so the claim holds under either.
        var doc = Procedure("SELECT short + 'x' AS a, label + N'!' AS b, short + label AS c, short + code AS d, short + id AS e FROM dbo.t;");
        Assert.Equal("varchar(6) nullable-column", Typed(doc, "a"));
        Assert.Equal("nvarchar(11)", Typed(doc, "b"));
        Assert.Equal("nvarchar(15) nullable-column", Typed(doc, "c"));
        Assert.Equal("varchar(9) nullable-column nullable-column", Typed(doc, "d"));
        Assert.StartsWith("unknown", Sql(doc, "e"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_plus_of_fixed_length_strings_is_fixed_length()
    {
        // Fixed_length_strings_plus_is_fixed_length (review round 1): char + char is char, nchar + char nchar; with a
        // varying operand it varies.
        var doc = Procedure("DECLARE @n nchar(2) = N'ab'; SELECT code + code AS a, @n + code AS b, code + 'x' AS c FROM dbo.t;");
        Assert.Equal("char(8) nullable-column", Typed(doc, "a"));
        Assert.Equal("nchar(6) nullable-column", Typed(doc, "b"));
        Assert.Equal("varchar(5) nullable-column", Typed(doc, "c"));
    }

    // ---- arithmetic ------------------------------------------------------------------------------------------------

    [Fact]
    public void Decimal_arithmetic_has_the_documented_type_and_overflows_only_by_division()
    {
        // Decimal_arithmetic_has_the_documented_precision_and_scale. Since slice 6j a division by a literal other than 0 cannot
        // divide by zero (NumericTests); by a column it can.
        var doc = Procedure("""
            SELECT amount + total AS a, total * 2 AS b, total / 3 AS c, n + 1 AS d, total - amount AS e, total + 0.5 AS f, -total AS g,
                   small + tiny AS h, amount * total AS i, ratio * 2 AS j, total / n AS k
            FROM dbo.t;
            """);
        Assert.Equal("decimal(20,4) nullable-column", Typed(doc, "a"));
        Assert.Equal("decimal(21,4)", Typed(doc, "b"));
        Assert.Equal("decimal(21,6)", Typed(doc, "c"));
        Assert.Equal("decimal(30,15) nullable-column arithmetic-overflow", Typed(doc, "k"));
        Assert.Equal("int nullable-column arithmetic-overflow", Typed(doc, "d"));
        Assert.Equal("decimal(20,4) nullable-column", Typed(doc, "e"));
        Assert.Equal("numeric(20,4)", Typed(doc, "f"));
        Assert.Equal("decimal(19,4)", Typed(doc, "g"));
        Assert.Equal("smallint nullable-column arithmetic-overflow", Typed(doc, "h"));
        Assert.Equal("decimal(30,6) nullable-column", Typed(doc, "i"));

        // Float arithmetic is read since slice 6i (FunctionTests): it can overflow.
        Assert.Equal("float nullable-column arithmetic-overflow", Typed(doc, "j"));
    }

    [Fact]
    public void Decimal_arithmetic_beyond_38_digits_is_capped_and_may_overflow()
    {
        // SQL Server then cuts the scale (slice 6j, NumericTests): decimal(38,4) * decimal(19,4) is decimal(38,6), whose 32
        // integer digits cannot hold the 49 the product may need.
        var doc = Procedure("SELECT SUM(total) * total AS a FROM dbo.t GROUP BY total;");
        Assert.Equal("decimal(38,6) arithmetic-overflow", Typed(doc, "a"));
    }
}
