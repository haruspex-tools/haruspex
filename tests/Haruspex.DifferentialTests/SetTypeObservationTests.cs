using System.Globalization;
using Haruspex.DifferentialTests.Server;

namespace Haruspex.DifferentialTests;

/// <summary>
/// How SQL Server types a column whose queries in a set operation (and the arguments of <c>COALESCE</c>, the results of a
/// <c>CASE</c>) have different types, and what converting each to that type does to its values: the rules for combining types rest on
/// (Haruspex.SqlServer.Inference's README.md, "Combining types"). Types are read as the server describes them
/// (<c>sys.dm_exec_describe_first_result_set</c>); what can be NULL, and what is an error instead, from values, by default and
/// under a caller's ANSI_WARNINGS and ARITHABORT off. <c>SetTypeGridTests</c> checks the type rules against the server over
/// every pair of a grid of types. Observed on SQL Server 2025 RTM-CU10.
/// </summary>
public class SetTypeObservationTests(SqlServerFixture server)
{
    private const string Off = "SET ANSI_WARNINGS OFF; SET ARITHABORT OFF; ";

    private Task<CallOutcome> Run(string call) => ObservedCall.Run(server, [], call);

    /// <summary>The server's description of a query's first column's type, or the error's number.</summary>
    private async Task<string> Describe(string select)
    {
        var query = select.Replace("'", "''", StringComparison.Ordinal);
        var outcome = await Run($"SELECT TOP (1) ISNULL(system_type_name, CONVERT(varchar(10), error_number)) AS c FROM sys.dm_exec_describe_first_result_set(N'{query}', NULL, 0) ORDER BY column_ordinal;");
        Assert.Null(outcome.Error);
        return outcome.Sets[0]["c: ".Length..];
    }

    /// <summary>
    /// The values a query's column <c>x</c> gives, read as varchar in their order, or the error's number, by default and under
    /// ANSI_WARNINGS and ARITHABORT off: <c>8115 / NULL;1.0000</c>.
    /// </summary>
    /// <param name="dated">Whether x is a date or time, read in style 121 (ODBC canonical with fractions).</param>
    private async Task<string> Both(string query, bool dated = false)
    {
        var convert = dated ? "CONVERT(varchar(100), x, 121)" : "CONVERT(varchar(100), x)";
        async Task<string> One(string options)
        {
            var outcome = await Run($"{options}SELECT ISNULL({convert}, 'NULL') AS v FROM ({query}) AS q;");
            return outcome.Error is { } error ? error.ToString(CultureInfo.InvariantCulture) : outcome.Sets[0]["v: ".Length..];
        }

        return $"{await One("SET ANSI_WARNINGS ON; SET ARITHABORT ON; ")} / {await One(Off)}";
    }

    // ---- types ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Exact_numbers_past_38_digits_keep_their_integer_digits_and_cut_the_scale()
    {
        // The integer digits are the most any query has (money 15, smallmoney 6, an int column 10, an integer literal its own),
        // the scale the largest; past 38 the precision is 38 and the scale is 38 less the integer digits, with no floor of 6
        // (unlike * and /), as for + and - without their one more digit.
        Assert.Equal("decimal(38,8)", await Describe("SELECT CAST(1 AS decimal(20,10)) AS x UNION ALL SELECT CAST(1 AS decimal(30,0))"));
        Assert.Equal("decimal(38,23)", await Describe("SELECT CAST(1 AS decimal(38,38)) AS x UNION ALL SELECT CAST(1 AS money)"));
        Assert.Equal("decimal(38,32)", await Describe("SELECT CAST(1 AS decimal(38,38)) AS x UNION ALL SELECT CAST(1 AS smallmoney)"));
        Assert.Equal("decimal(38,28)", await Describe("SELECT CAST(1 AS decimal(38,38)) AS x UNION ALL SELECT CAST(1 AS int)"));
        Assert.Equal("decimal(38,0)", await Describe("SELECT CAST(1 AS decimal(38,38)) AS x UNION ALL SELECT CAST(1 AS decimal(38,0))"));
        Assert.Equal("decimal(38,37)", await Describe("SELECT CAST(1 AS decimal(38,38)) AS x UNION ALL SELECT 1"));
        Assert.Equal("decimal(38,8)", await Describe("SELECT COALESCE(CAST(1 AS decimal(20,10)), CAST(1 AS decimal(30,0))) AS x"));
    }

    /// <summary>A source of one column of each type the chain tests read, none of them constant (TOP (0) over a catalog view).</summary>
    private const string Source = """
        (SELECT TOP (0) CAST(1 AS bigint) AS bi, CAST(1 AS int) AS i, CAST(1 AS money) AS m, CAST(1 AS smallmoney) AS sm,
                CAST(1 AS decimal(5,2)) AS d52, CAST(1 AS decimal(38,38)) AS d3838, CAST('2000-01-01' AS date) AS dt,
                CAST('10:00' AS time(3)) AS tm3, CAST('10:00' AS time(7)) AS tm7, CAST('2000-01-01' AS datetime) AS dtm,
                CAST('2000-01-01' AS datetime2(3)) AS dt23, CAST('2000-01-01' AS datetimeoffset(3)) AS dto3, NEWID() AS u,
                CAST('a' AS varchar(10)) AS v10
         FROM sys.objects) AS t
        """;

    private Task<string> Chain(params string[] columns) =>
        Describe(string.Join(" UNION ALL ", columns.Select((c, i) => $"SELECT {c}{(i == 0 ? " AS x" : "")} FROM {Source}")));

    [Fact]
    public async Task A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal()
    {
        // Not one computation over every query at once but a fold, left to right. An integer and money meeting
        // before the decimal give money, whose 15 digits (smallmoney's 6) the decimal then counts; the integer's are gone.
        Assert.Equal("decimal(19,4)", await Chain("bi", "m", "d52"));
        Assert.Equal("decimal(23,4)", await Chain("bi", "d52", "m"));
        Assert.Equal("decimal(23,4)", await Chain("d52", "bi", "m"));
        Assert.Equal("decimal(10,4)", await Chain("i", "sm", "d52"));
        Assert.Equal("decimal(10,4)", await Chain("sm", "i", "d52"));
        Assert.Equal("decimal(14,4)", await Chain("i", "d52", "sm"));
        Assert.Equal("decimal(38,23)", await Chain("bi", "m", "d3838"));
        Assert.Equal("decimal(19,4)", await Describe($"SELECT COALESCE(bi, m, d52) AS x FROM {Source}"));
        Assert.Equal("decimal(23,4)", await Describe($"SELECT COALESCE(d52, bi, m) AS x FROM {Source}"));

        // So the bigint is converted to decimal(19,4), which overflows at its ends.
        Assert.Equal(
            "8115 / NULL;1.0000;1.0000",
            await Both("SELECT CAST(9223372036854775807 AS bigint) AS x UNION ALL SELECT CAST(1 AS money) UNION ALL SELECT CAST(1 AS decimal(5,2))"));
    }

    [Fact]
    public async Task A_chain_takes_the_most_fractional_digits_of_every_date_and_time()
    {
        // Unlike the integer digits above, a time's fractional digits are kept through a date or datetime before it.
        Assert.Equal("datetime2(7)", await Chain("dt", "tm7", "dt23"));
        Assert.Equal("datetime2(7)", await Chain("dt23", "dt", "tm7"));
        Assert.Equal("datetime2(7)", await Chain("tm7", "dtm", "dt23"));
        Assert.Equal("datetimeoffset(7)", await Chain("dtm", "tm7", "dto3"));
    }

    [Fact]
    public async Task A_chain_is_refused_only_when_a_value_cannot_become_its_type()
    {
        // An int and a date alone are refused, but beside a datetime both become datetime; so are a date and
        // a time beside a datetime2. A uniqueidentifier that must become an int is refused wherever it stands.
        Assert.Equal("datetime", await Chain("1", "dt", "dtm"));
        Assert.Equal("datetime", await Chain("dtm", "dt", "1"));
        Assert.Equal("datetime", await Chain("i", "tm3", "dtm"));
        Assert.Equal("datetime2(7)", await Chain("dt23", "dt", "tm7"));
        Assert.Equal("datetime", await Describe($"SELECT COALESCE(1, dt, dtm) AS x FROM {Source}"));
        Assert.Equal("206", await Chain("i", "u", "v10"));
        Assert.Equal("206", await Chain("v10", "u", "i"));
        Assert.Equal("206", await Chain("tm3", "i", "dt"));
    }

    [Fact]
    public async Task An_int_constant_counts_its_own_digits_beside_a_decimal()
    {
        // Beside decimal(38,38), an int column counts 10 integer digits (decimal(38,28)); an int the server
        // treats as a constant counts its own: a literal, the NULL cast to int (1), a derived table's literal column, a CASE
        // or IIF with a constant condition (folded to that literal), COALESCE(NULL, 1). Arithmetic, a scalar subquery, a
        // CAST of a number, ISNULL, a CASE with a column in its condition and a set operation of literals are not folded so.
        // Other typed NULLs count their type's digits. Decimal arithmetic counts the same way.
        Assert.Equal("decimal(38,37)", await Chain("d3838", "CAST(NULL AS int)"));
        Assert.Equal("decimal(38,37)", await Chain("d3838", "CONVERT(int, NULL)"));
        Assert.Equal("decimal(38,37)", await Chain("d3838", "TRY_CAST(NULL AS int)"));
        Assert.Equal("decimal(38,37)", await Chain("d3838", "CAST(CAST(NULL AS int) AS int)"));
        Assert.Equal("decimal(38,33)", await Chain("d3838", "CAST(NULL AS smallint)"));
        Assert.Equal("decimal(38,19)", await Chain("d3838", "CAST(NULL AS bigint)"));
        Assert.Equal("decimal(38,36)", await Chain("d3838", "-(12)"));
        Assert.Equal("decimal(38,37)", await Chain("1", "v10", "d3838"));
        Assert.Equal("decimal(38,37)", await Chain("v10", "1", "d3838"));
        Assert.Equal("decimal(38,37)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT q.y FROM (SELECT 1 AS y FROM {Source}) AS q"));
        Assert.Equal("decimal(38,37)", await Chain("d3838", "IIF(1 = 1, 1, 2)"));
        Assert.Equal("decimal(38,36)", await Chain("d3838", "CASE WHEN 1 = 1 THEN 12 ELSE 2 END"));
        Assert.Equal("decimal(38,37)", await Chain("d3838", "COALESCE(NULL, 1)"));
        Assert.Equal("decimal(38,28)", await Chain("d3838", "CAST(2 AS int)"));
        Assert.Equal("decimal(38,28)", await Chain("d3838", "1 + 1"));
        Assert.Equal("decimal(38,28)", await Chain("d3838", "(SELECT 1)"));
        Assert.Equal("decimal(38,28)", await Chain("d3838", "ISNULL(NULL, 1)"));
        Assert.Equal("decimal(38,28)", await Chain("d3838", "IIF(i > 0, 1, 2)"));
        Assert.Equal("decimal(38,28)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT q.y FROM (SELECT 1 AS y UNION ALL SELECT 2) AS q"));
        Assert.Equal("decimal(38,37)", await Describe($"SELECT d3838 + CAST(NULL AS int) AS x FROM {Source}"));
        Assert.Equal("decimal(38,36)", await Describe($"SELECT d3838 * CAST(NULL AS int) AS x FROM {Source}"));
        Assert.Equal("decimal(38,37)", await Describe($"SELECT COALESCE(d3838, CAST(NULL AS int)) AS x FROM {Source}"));
    }

    [Fact]
    public async Task A_recursive_ctes_column_is_not_a_constant_even_when_every_member_is_one_literal()
    {
        // A CTE that does not recurse carries its literal column as a derived table does, counting the literal's own digits.
        // One that recurses counts its type's 10, whatever its members are: its rows come from the recursion, and the server
        // folds none of them to the literal.
        Assert.Equal("decimal(38,37)", await Describe($"WITH c AS (SELECT 1 AS n) SELECT d3838 AS x FROM {Source} UNION ALL SELECT n FROM c"));
        Assert.Equal("decimal(38,28)", await Describe($"WITH c AS (SELECT 1 AS n UNION ALL SELECT 1 FROM c WHERE 1 = 0) SELECT d3838 AS x FROM {Source} UNION ALL SELECT n FROM c"));
        Assert.Equal("decimal(38,28)", await Describe($"WITH c AS (SELECT 12 AS n UNION ALL SELECT n + 1 FROM c WHERE n < 20) SELECT d3838 AS x FROM {Source} UNION ALL SELECT n FROM c"));
    }

    [Fact]
    public async Task A_values_lists_int_column_is_a_constant_in_one_row_only()
    {
        // A VALUES list of one row is folded to its literal, as a derived table's literal column is; of several rows it is
        // not, even when every row is the same literal, and the column counts the type's 10 digits.
        Assert.Equal("decimal(38,37)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT v.y FROM (VALUES (1)) AS v(y)"));
        Assert.Equal("decimal(38,28)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT v.y FROM (VALUES (1), (1)) AS v(y)"));
        Assert.Equal("decimal(38,28)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT v.y FROM (VALUES (1), (22)) AS v(y)"));
        Assert.Equal("decimal(38,28)", await Describe($"SELECT d3838 AS x FROM {Source} UNION ALL SELECT v.y FROM (VALUES (22), (1)) AS v(y)"));
    }

    [Fact]
    public async Task A_cut_scale_rounds_but_never_overflows_since_the_integer_digits_are_kept()
    {
        // A query whose scale is cut has fewer integer digits than the column keeps (or it would not need cutting), so rounding
        // up into one more digit still fits.
        Assert.Equal(
            "1;5 / 1;5",
            await Both("SELECT CAST(0.99999999999999999999999999999999999999 AS decimal(38,38)) AS x UNION ALL SELECT CAST(5 AS decimal(38,0))"));
        Assert.Equal(
            "100000000000000000000.00000000;1.00000000 / 100000000000000000000.00000000;1.00000000",
            await Both("SELECT CAST(99999999999999999999.9999999999 AS decimal(30,10)) AS x UNION ALL SELECT CAST(1 AS decimal(30,0))"));
    }

    [Fact]
    public async Task A_decimal_is_named_as_the_first_decimal_query()
    {
        Assert.Equal("numeric(9,2)", await Describe("SELECT CAST(1 AS numeric(9,2)) AS x UNION ALL SELECT CAST(1 AS decimal(5,2))"));
        Assert.Equal("decimal(9,2)", await Describe("SELECT CAST(1 AS decimal(5,2)) AS x UNION ALL SELECT CAST(1 AS numeric(9,2))"));
        Assert.Equal("numeric(12,2)", await Describe("SELECT CAST(1 AS int) AS x UNION ALL SELECT CAST(1 AS numeric(9,2)) UNION ALL SELECT CAST(1 AS decimal(5,2))"));
    }

    [Fact]
    public async Task A_bit_takes_the_other_types_and_a_string_the_number_date_or_uniqueidentifier_beside_it()
    {
        // bit is below every number in precedence and is converted to the other's own type, a decimal's precision and scale too;
        // a string is below bit and every other type.
        Assert.Equal("decimal(5,2)", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(1 AS decimal(5,2))"));
        Assert.Equal("tinyint", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(1 AS tinyint)"));
        Assert.Equal("smallmoney", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(1 AS smallmoney)"));
        Assert.Equal("real", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(1 AS real)"));
        Assert.Equal("bit", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST('1' AS varchar(10))"));
        Assert.Equal("int", await Describe("SELECT CAST('1' AS nvarchar(20)) AS x UNION ALL SELECT CAST(1 AS int)"));
        Assert.Equal("decimal(5,2)", await Describe("SELECT CAST('1' AS varchar(max)) AS x UNION ALL SELECT CAST(1 AS decimal(5,2))"));
        Assert.Equal("time(3)", await Describe("SELECT CAST('1' AS char(10)) AS x UNION ALL SELECT CAST('10:00' AS time(3))"));
        Assert.Equal("uniqueidentifier", await Describe("SELECT CAST('1' AS varchar(10)) AS x UNION ALL SELECT NEWID()"));
    }

    [Fact]
    public async Task Integers_money_and_float_follow_precedence_whatever_the_range()
    {
        // smallmoney is above bigint, though it holds far less: the type is smallmoney and bigint's values may overflow it.
        Assert.Equal("smallmoney", await Describe("SELECT CAST(1 AS bigint) AS x UNION ALL SELECT CAST(1 AS smallmoney)"));
        Assert.Equal("money", await Describe("SELECT CAST(1 AS bigint) AS x UNION ALL SELECT CAST(1 AS money)"));
        Assert.Equal("money", await Describe("SELECT CAST(1 AS smallmoney) AS x UNION ALL SELECT CAST(1 AS money)"));
        Assert.Equal("real", await Describe("SELECT CAST(1 AS decimal(38,0)) AS x UNION ALL SELECT CAST(1 AS real)"));
        Assert.Equal("float", await Describe("SELECT CAST(1 AS real) AS x UNION ALL SELECT CAST(1 AS float)"));
    }

    [Fact]
    public async Task Strings_are_national_if_any_is_varying_if_any_is_and_national_ones_stop_at_4000()
    {
        Assert.Equal("nvarchar(10)", await Describe("SELECT CAST('a' AS varchar(10)) AS x UNION ALL SELECT CAST('a' AS nchar(5))"));
        Assert.Equal("nchar(10)", await Describe("SELECT CAST('a' AS char(10)) AS x UNION ALL SELECT CAST('a' AS nchar(5))"));
        Assert.Equal("nvarchar(4000)", await Describe("SELECT CAST('a' AS varchar(5000)) AS x UNION ALL SELECT CAST('a' AS nvarchar(20))"));
        Assert.Equal("nchar(4000)", await Describe("SELECT CAST('a' AS char(8000)) AS x UNION ALL SELECT CAST('a' AS nchar(5))"));
        Assert.Equal("nvarchar(max)", await Describe("SELECT CAST('a' AS varchar(max)) AS x UNION ALL SELECT CAST('a' AS nchar(5))"));
        Assert.Equal("varchar(max)", await Describe("SELECT CAST('a' AS varchar(max)) AS x UNION ALL SELECT CAST('a' AS char(10))"));
    }

    [Fact]
    public async Task A_longer_string_is_cut_to_4000_characters_beside_a_national_one_without_an_error()
    {
        // varchar(5000) beside nchar(5) is nvarchar(4000): its 5000 characters are cut short, as a conversion to a shorter string
        // is, by default too; nchar(5)'s value keeps its padding.
        var outcome = await Run("""
            SELECT LEN(x) AS n, DATALENGTH(x) AS bytes
            FROM (SELECT CAST(REPLICATE('a', 5000) AS varchar(5000)) AS x UNION ALL SELECT CAST(N'x' AS nchar(5))) AS q ORDER BY n DESC;
            """);
        Assert.Equal("n,bytes: 4000,8000;1,10", outcome.ToString());
    }

    [Fact]
    public async Task Date_and_time_types_take_the_highest_with_the_most_fractional_digits()
    {
        // datetimeoffset, datetime2, datetime, smalldatetime, date, time, in that order; datetimeoffset and datetime2 take the
        // most fractional digits of any datetimeoffset, datetime2 or time, and 3 for a datetime; a time takes the most of times.
        Assert.Equal("datetimeoffset(7)", await Describe("SELECT CAST('10:00' AS time(7)) AS x UNION ALL SELECT CAST('2000-01-01' AS datetimeoffset(3))"));
        Assert.Equal("datetimeoffset(3)", await Describe("SELECT CAST('2000-01-01' AS datetime) AS x UNION ALL SELECT CAST('2000-01-01' AS datetimeoffset(0))"));
        Assert.Equal("datetimeoffset(3)", await Describe("SELECT CAST('2000-01-01' AS date) AS x UNION ALL SELECT CAST('2000-01-01' AS datetimeoffset(3))"));
        Assert.Equal("datetime2(7)", await Describe("SELECT CAST('10:00' AS time(7)) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime2(3))"));
        Assert.Equal("datetime2(3)", await Describe("SELECT CAST('2000-01-01' AS datetime) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime2(0))"));
        Assert.Equal("datetime", await Describe("SELECT CAST('10:00' AS time(7)) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime)"));
        Assert.Equal("smalldatetime", await Describe("SELECT CAST('10:00' AS time(7)) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)"));
        Assert.Equal("smalldatetime", await Describe("SELECT CAST('2000-01-01' AS date) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)"));
        Assert.Equal("time(7)", await Describe("SELECT CAST('10:00' AS time(3)) AS x UNION ALL SELECT CAST('10:00' AS time(7))"));

        // A number beside datetime or smalldatetime takes that type, as arithmetic on them does.
        Assert.Equal("datetime", await Describe("SELECT CAST(1 AS float) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime)"));
        Assert.Equal("smalldatetime", await Describe("SELECT CAST(1 AS decimal(5,2)) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)"));
    }

    [Fact]
    public async Task Coalesce_of_a_string_literal_first_is_folded_to_it_beside_a_string()
    {
        // Not a set operation's rule but COALESCE's, which the grid would otherwise show: a string literal first, beside a
        // string column that would not make it national or max, is folded to the literal and typed by it alone; a number
        // literal, or the literal anywhere else, is not.
        Assert.Equal("varchar(3)", await Describe("SELECT COALESCE('abc', x) AS x FROM (SELECT TOP (0) CAST('a' AS varchar(10)) AS x FROM sys.objects) AS t"));
        Assert.Equal("nvarchar(1)", await Describe("SELECT COALESCE(N'x', x) AS x FROM (SELECT TOP (0) CAST('a' AS varchar(10)) AS x FROM sys.objects) AS t"));
        Assert.Equal("nvarchar(20)", await Describe("SELECT COALESCE('abc', x) AS x FROM (SELECT TOP (0) CAST('a' AS nvarchar(20)) AS x FROM sys.objects) AS t"));
        Assert.Equal("varchar(max)", await Describe("SELECT COALESCE('abc', x) AS x FROM (SELECT TOP (0) CAST('a' AS varchar(max)) AS x FROM sys.objects) AS t"));
        Assert.Equal("varchar(10)", await Describe("SELECT COALESCE(x, 'abc') AS x FROM (SELECT TOP (0) CAST('a' AS varchar(10)) AS x FROM sys.objects) AS t"));
        Assert.Equal("numeric(5,2)", await Describe("SELECT COALESCE(2.5, x) AS x FROM (SELECT TOP (0) CAST(1 AS decimal(5,2)) AS x FROM sys.objects) AS t"));
    }

    [Fact]
    public async Task Types_without_an_implicit_conversion_are_refused_with_an_operand_type_clash()
    {
        // 206 "Operand type clash: int is incompatible with uniqueidentifier"; the procedure is still created (deferred), and
        // fails when run. COALESCE refuses the same pairs.
        Assert.Equal("206", await Describe("SELECT CAST(1 AS int) AS x UNION ALL SELECT NEWID()"));
        Assert.Equal("206", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT NEWID()"));
        Assert.Equal("206", await Describe("SELECT CAST('2000-01-01' AS date) AS x UNION ALL SELECT NEWID()"));
        Assert.Equal("206", await Describe("SELECT CAST(1 AS int) AS x UNION ALL SELECT CAST('2000-01-01' AS date)"));
        Assert.Equal("206", await Describe("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST('10:00' AS time)"));
        Assert.Equal("206", await Describe("SELECT CAST(1 AS money) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime2)"));
        Assert.Equal("206", await Describe("SELECT CAST(1 AS float) AS x UNION ALL SELECT CAST('2000-01-01' AS datetimeoffset)"));
        Assert.Equal("206", await Describe("SELECT CAST('2000-01-01' AS date) AS x UNION ALL SELECT CAST('10:00' AS time)"));
        Assert.Equal("206", await Describe("SELECT COALESCE(CAST(1 AS int), NEWID()) AS x"));
    }

    // ---- values ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_conversion_that_overflows_is_an_error_by_default_and_null_under_the_callers_options()
    {
        // As a CAST's (ExpressionObservationTests): 8115 or 242 (out of range) or 244/248 (a string's number), and NULL under
        // ANSI_WARNINGS and ARITHABORT off, in a set operation, COALESCE and CASE alike.
        Assert.Equal("8115 / NULL;1.00", await Both("SELECT CAST(3000000000 AS bigint) AS x UNION ALL SELECT CAST(1 AS smallmoney)"));
        Assert.Equal("8115 / NULL;1.00", await Both("SELECT CAST(9000000000000000000 AS bigint) AS x UNION ALL SELECT CAST(1 AS money)"));
        Assert.Equal("8115 / NULL;2000-01-01 00:00:00.000", await Both("SELECT CAST(1e300 AS float) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime)", dated: true));
        Assert.Equal("8115 / NULL;0.10000000000000000000000000000000000000", await Both("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(0.1 AS decimal(38,38))"));
        Assert.Equal("242 / NULL;2000-01-01 00:00:00.000", await Both("SELECT CAST('0001-01-01' AS date) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)", dated: true));
        Assert.Equal("242 / NULL;2000-01-01 00:00:00.000", await Both("SELECT CAST('0001-01-01' AS date) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime)", dated: true));
        Assert.Equal("8115 / NULL;2000-01-01 00:00:00.000", await Both("SELECT CAST(-32768 AS smallint) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)", dated: true));
        Assert.Equal("244 / NULL;255", await Both("SELECT CAST('300' AS varchar(10)) AS x UNION ALL SELECT CAST(255 AS tinyint)"));
        Assert.Equal("248 / NULL;1", await Both("SELECT CAST('3000000000' AS varchar(20)) AS x UNION ALL SELECT CAST(1 AS int)"));
        Assert.Equal("8115 / NULL;1", await Both("SELECT CAST('1e400' AS varchar(10)) AS x UNION ALL SELECT CAST(1 AS float)"));
        Assert.Equal("242 / NULL;2000-01-01 00:00:00.000", await Both("SELECT CAST('0001-01-01' AS varchar(10)) AS x UNION ALL SELECT CAST('2000-01-01' AS smalldatetime)", dated: true));
        Assert.Equal("8115 / NULL", await Both("SELECT COALESCE(CAST(3000000000 AS bigint), CAST(1 AS smallmoney)) AS x"));
        Assert.Equal("8115 / NULL", await Both("SELECT CASE WHEN x > 0 THEN CAST(3000000000 AS bigint) ELSE CAST(1 AS smallmoney) END AS x FROM (SELECT 1 AS x) AS r"));
    }

    [Fact]
    public async Task A_set_operations_overflow_under_the_callers_options_is_null_on_the_server_and_misread_by_sqlclient()
    {
        // Over a table (not constants, which the server folds), under ANSI_WARNINGS and ARITHABORT off, the server's value is
        // NULL, as an expression over it shows; but read directly, the row reaches SqlClient malformed: it reads a wrong value
        // (-78847.5648 for a bigint at its greatest as smallmoney, 53504 for '3000000000' as int) and then fails with
        // "Internal connection fatal error", for every type tried (smallmoney, datetime, int, smalldatetime). COALESCE and CASE
        // over the same rows come out NULL. The fixture reads such columns through a CONVERT.
        // Not asserted here, since a dead connection would take the test's session with it.
        const string Units = """
            CREATE TABLE dbo.obs_units (id int NOT NULL PRIMARY KEY, units bigint NOT NULL, fee smallmoney NOT NULL);
            INSERT INTO dbo.obs_units VALUES (1, 6, 1.5), (2, 3000000000, 2);
            """;
        async Task<string> One(string options, string expression, string from)
        {
            var outcome = await ObservedCall.Run(server, [Units], $"{options}SELECT ISNULL(CONVERT(varchar(30), {expression}), 'NULL') AS v FROM {from} ORDER BY id;");
            return outcome.Error is { } error ? error.ToString(CultureInfo.InvariantCulture) : outcome.Sets[0]["v: ".Length..];
        }

        const string Union = "(SELECT u.id, u.units AS x FROM dbo.obs_units AS u UNION ALL SELECT u.id + 10, u.fee FROM dbo.obs_units AS u) AS q";
        Assert.Equal("8115", await One("SET ANSI_WARNINGS ON; SET ARITHABORT ON; ", "CONVERT(smallmoney, x)", Union));
        Assert.Equal("6.00;NULL;1.50;2.00", await One(Off, "CONVERT(smallmoney, x)", Union));
        Assert.Equal("6.00;NULL", await One(Off, "COALESCE(units, fee)", "dbo.obs_units"));
        Assert.Equal("6.00;NULL", await One(Off, "CASE WHEN id > 0 THEN units ELSE fee END", "dbo.obs_units"));
    }

    [Fact]
    public async Task A_string_that_is_not_a_value_of_the_type_is_an_error_even_under_the_callers_options()
    {
        // A failed conversion is never NULL (8114/245/241/8169); a string into uniqueidentifier is cut to its first 36
        // characters, so it either converts or fails.
        Assert.Equal("245 / 245", await Both("SELECT CAST('abc' AS varchar(10)) AS x UNION ALL SELECT CAST(1 AS int)"));
        Assert.Equal("245 / 245", await Both("SELECT CAST('abc' AS varchar(10)) AS x UNION ALL SELECT CAST(1 AS bit)"));
        Assert.Equal("241 / 241", await Both("SELECT CAST('2020-13-45' AS varchar(10)) AS x UNION ALL SELECT CAST('2000-01-01' AS date)", dated: true));
        Assert.Equal("8169 / 8169", await Both("SELECT CAST('zz' AS varchar(10)) AS x UNION ALL SELECT CAST('6F9619FF-8B86-D011-B42D-00C04FC964FF' AS uniqueidentifier)"));
        Assert.Equal(
            "6F9619FF-8B86-D011-B42D-00C04FC964FF;6F9619FF-8B86-D011-B42D-00C04FC964FF / 6F9619FF-8B86-D011-B42D-00C04FC964FF;6F9619FF-8B86-D011-B42D-00C04FC964FF",
            await Both("SELECT CAST('6F9619FF-8B86-D011-B42D-00C04FC964FF and more' AS varchar(50)) AS x UNION ALL SELECT CAST('6F9619FF-8B86-D011-B42D-00C04FC964FF' AS uniqueidentifier)"));
    }

    [Fact]
    public async Task A_conversion_that_cannot_overflow_keeps_every_value_at_its_edges()
    {
        // The conversions the model counts as safe, each at its type's ends: a time is that time on 1 January 1900 (rounding
        // into the 2nd for datetime and smalldatetime); a tinyint or bit is that many days from 1900, a smallint within datetime's
        // range; date, datetime2, datetime and smalldatetime into datetimeoffset at offset 0 (a datetime by its 1/300 s ticks, so .997 is .9966667).
        Assert.Equal(
            "1900-01-01 23:59:59.9999999;2000-01-01 00:00:00.0000000 / 1900-01-01 23:59:59.9999999;2000-01-01 00:00:00.0000000",
            await Both("SELECT CAST('23:59:59.9999999' AS time(7)) AS x UNION ALL SELECT CAST('2000-01-01' AS datetime2(7))", dated: true));
        Assert.Equal(
            "1900-01-02 00:00:00.000;NULL / 1900-01-02 00:00:00.000;NULL",
            await Both("SELECT CAST('23:59:59.9999999' AS time(7)) AS x UNION ALL SELECT CAST(NULL AS datetime)", dated: true));
        Assert.Equal(
            "1900-01-02 00:00:00.000;NULL / 1900-01-02 00:00:00.000;NULL",
            await Both("SELECT CAST('23:59:59.9999999' AS time(7)) AS x UNION ALL SELECT CAST(NULL AS smalldatetime)", dated: true));
        Assert.Equal(
            "1900-01-01 23:59:59.9999999 +00:00;NULL / 1900-01-01 23:59:59.9999999 +00:00;NULL",
            await Both("SELECT CAST('23:59:59.9999999' AS time(7)) AS x UNION ALL SELECT CAST(NULL AS datetimeoffset(7))", dated: true));
        Assert.Equal(
            "1810-04-15 00:00:00.000;1989-09-18 00:00:00.000;NULL / 1810-04-15 00:00:00.000;1989-09-18 00:00:00.000;NULL",
            await Both("SELECT CAST(-32768 AS smallint) AS x UNION ALL SELECT CAST(32767 AS smallint) UNION ALL SELECT CAST(NULL AS datetime)", dated: true));
        Assert.Equal(
            "1900-01-02 00:00:00.000;1900-09-13 00:00:00.000;NULL / 1900-01-02 00:00:00.000;1900-09-13 00:00:00.000;NULL",
            await Both("SELECT CAST(1 AS bit) AS x UNION ALL SELECT CAST(255 AS tinyint) UNION ALL SELECT CAST(NULL AS smalldatetime)", dated: true));
        Assert.Equal(
            "9999-12-31 23:59:59.9999999 +00:00;9999-12-31 00:00:00.0000000 +00:00;0001-01-01 00:00:00.0000000 +00:00;9999-12-31 23:59:59.9966667 +00:00;2079-06-06 23:59:00.0000000 +00:00;NULL / "
            + "9999-12-31 23:59:59.9999999 +00:00;9999-12-31 00:00:00.0000000 +00:00;0001-01-01 00:00:00.0000000 +00:00;9999-12-31 23:59:59.9966667 +00:00;2079-06-06 23:59:00.0000000 +00:00;NULL",
            await Both("""
                SELECT CAST('9999-12-31 23:59:59.9999999' AS datetime2(7)) AS x UNION ALL SELECT CAST('9999-12-31' AS date)
                UNION ALL SELECT CAST('0001-01-01' AS date) UNION ALL SELECT CAST('9999-12-31 23:59:59.997' AS datetime)
                UNION ALL SELECT CAST('2079-06-06 23:59' AS smalldatetime) UNION ALL SELECT CAST(NULL AS datetimeoffset(7))
                """, dated: true));
    }
}
