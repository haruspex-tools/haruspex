using Haruspex.DifferentialTests.Server;
using Microsoft.Data.SqlClient;

namespace Haruspex.DifferentialTests;

/// <summary>
/// What SQL Server's own <c>sp_describe_undeclared_parameters</c> says a query file's undeclared parameters are, use by use, and
/// that haruspex says the same or says it doesn't know (decision 0059): each rule of <c>ParameterRules</c> is observed here,
/// each case below with its server's answer. The queries read the invented shop
/// the unit tests read (<c>InferQuery.Shop</c>), created in a session of its own under a schema no fixture has, and rolled back.
/// </summary>
public class QueryParameterObservationTests(SqlServerFixture server)
{
    /// <summary>The schema the shop is created under on the server, apart from the fixtures' (the haberdashery's is <c>shop</c>).</summary>
    private const string Schema = "obs_query_shop";

    private static string OnServer(string sql) => sql.Replace("shop.", Schema + ".", StringComparison.Ordinal);

    /// <summary>
    /// The server's answer for <paramref name="query"/>: each parameter as <c>@name type</c>, a decimal written <c>decimal</c> where the
    /// server says <c>numeric</c>, which is the same type; or the number of the error it refuses the query with.
    /// </summary>
    private async Task<(IReadOnlyList<string> Parameters, int? Error)> Described(string query)
    {
        server.Require();
        var token = TestContext.Current.CancellationToken;
        await using var session = await server.WriteAsync(token);
        foreach (var batch in new[] { $"CREATE SCHEMA {Schema};" }.Concat(InferQuery.Shop.Split("\nGO\n").Select(OnServer)))
        {
            await using var create = session.Command(batch);
            await create.ExecuteNonQueryAsync(token);
        }

        await using var describe = session.Command("EXEC sys.sp_describe_undeclared_parameters @tsql = @q;");
        describe.Parameters.Add(new SqlParameter("@q", System.Data.SqlDbType.NVarChar, -1) { Value = OnServer(query) });
        try
        {
            var parameters = new List<string>();
            await using var reader = await describe.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                parameters.Add(reader.GetString(reader.GetOrdinal("name")) + " " + reader.GetString(reader.GetOrdinal("suggested_system_type_name")).Replace("numeric(", "decimal(", StringComparison.Ordinal));
            return (parameters, null);
        }
        catch (SqlException e)
        {
            return ([], e.Number);
        }
    }

    /// <summary>What haruspex says, as <see cref="Described"/> writes the server's answer, with <c>unknown</c> for a parameter it doesn't decide.</summary>
    private static IReadOnlyList<string> Inferred(string query) =>
        [.. InferQuery.Infer(InferQuery.Shop, query).Parameters().Select(p => p.Replace("numeric(", "decimal(", StringComparison.Ordinal))];

    /// <summary>The server and haruspex both give <paramref name="expected"/>.</summary>
    private async Task Both(string query, params string[] expected)
    {
        var (described, error) = await Described(query);
        Assert.Null(error);
        Assert.Equal(expected, described);
        Assert.Equal(expected, Inferred(query));
    }

    // ---- comparisons ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("c.customer_id = @p", "int")]
    [InlineData("@p = c.customer_id", "int")]
    [InlineData("c.name = @p", "nvarchar(80)")]
    [InlineData("c.email <> @p", "varchar(120)")]
    [InlineData("c.tier != @p", "char(1)")]
    [InlineData("c.code = @p", "nchar(4)")]
    [InlineData("c.notes = @p", "nvarchar(max)")]
    [InlineData("c.balance = @p", "decimal(10,2)")]
    [InlineData("c.seen = @p", "datetime2(3)")]
    [InlineData("c.flags = @p", "tinyint")]
    [InlineData("c.rate = @p", "float")]
    [InlineData("c.credit = @p", "money")]
    [InlineData("c.customer_id IN (@p, 3)", "int")]
    public async Task Equality_with_a_column_takes_its_type_exactly(string condition, string type) =>
        await Both($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};", "@p " + type);

    [Theory]
    [InlineData("c.customer_id > @p", "int")]
    [InlineData("c.flags < @p", "tinyint")]
    [InlineData("c.rate >= @p", "float")]
    [InlineData("c.credit <= @p", "money")]
    [InlineData("c.joined > @p", "date")]
    [InlineData("c.stamp > @p", "datetime")]
    [InlineData("c.balance > @p", "decimal(38,19)")]
    [InlineData("@p < c.balance", "decimal(38,19)")]
    [InlineData("c.name > @p", "nvarchar(4000)")]
    [InlineData("c.code > @p", "nvarchar(4000)")]
    [InlineData("c.email > @p", "varchar(8000)")]
    [InlineData("c.tier > @p", "varchar(8000)")]
    [InlineData("c.notes > @p", "nvarchar(max)")]
    [InlineData("c.seen > @p", "datetime2(7)")]
    [InlineData("c.balance BETWEEN @p AND 100", "decimal(38,19)")]
    [InlineData("@p BETWEEN 1 AND 10", "int")]
    public async Task Ordering_keeps_a_type_of_fixed_size_and_widens_a_decimal_a_string_and_a_time(string condition, string type) =>
        await Both($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};", "@p " + type);

    [Theory]
    [InlineData("c.name LIKE @p", "nvarchar(4000)")]
    [InlineData("c.notes LIKE @p", "nvarchar(4000)")]
    [InlineData("c.email LIKE @p", "varchar(8000)")]
    [InlineData("c.tier LIKE @p", "varchar(8000)")]
    [InlineData("@p LIKE 'a%'", "varchar(8000)")]
    [InlineData("@p LIKE N'a%'", "nvarchar(4000)")]
    public async Task Like_takes_the_longest_string_of_the_other_side_s_kind(string condition, string type) =>
        await Both($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};", "@p " + type);

    [Theory]
    [InlineData("1", "int")]
    [InlineData("-1", "int")]
    [InlineData("'abc'", "varchar(3)")]
    [InlineData("N'x'", "nvarchar(1)")]
    [InlineData("1.50", "decimal(3,2)")]
    [InlineData("3000000000", "decimal(10,0)")]
    [InlineData("1e3", "float")]
    [InlineData("$5", "money")]
    public async Task Equality_with_a_literal_takes_the_literal_s_type(string literal, string type) =>
        await Both($"IF @p = {literal} SELECT 1 AS one;", "@p " + type);

    // ---- arithmetic, counts and calls ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("o.qty - @p", "smallint")]
    [InlineData("o.qty % @p", "smallint")]
    [InlineData("o.qty & @p", "smallint")]
    [InlineData("@p * o.total", "decimal(38,19)")]
    [InlineData("o.total / @p", "decimal(38,19)")]
    [InlineData("c.rate * @p", "float")]
    [InlineData("c.credit + @p", "money")]
    [InlineData("c.stamp + @p", "datetime")]
    [InlineData("c.name + @p", "nvarchar(4000)")]
    [InlineData("c.notes + @p", "nvarchar(4000)")]
    [InlineData("c.email + @p", "varchar(8000)")]
    [InlineData("@p + 1", "int")]
    [InlineData("@p + 1.5", "decimal(38,19)")]
    public async Task Arithmetic_in_a_select_list_takes_the_other_operand_s_type_by_its_kind(string expression, string type) =>
        await Both($"SELECT {expression} AS v FROM shop.orders AS o JOIN shop.customers AS c ON c.customer_id = o.customer_id;", "@p " + type);

    [Fact]
    public async Task Arithmetic_inside_a_comparison_is_typed_from_the_comparison_which_haruspex_leaves_unknown()
    {
        const string query = "SELECT 1 AS one FROM shop.orders AS o WHERE o.total = @p * 2;";

        Assert.Equal(["@p decimal(38,19)"], (await Described(query)).Parameters);
        Assert.Equal(["@p unknown"], Inferred(query));
    }

    [Fact]
    public async Task Top_percent_takes_float() =>
        await Both("SELECT TOP (@p) PERCENT c.name FROM shop.customers AS c;", "@p float");

    [Theory]
    [InlineData("@p IN ('a', 'bb')", "varchar(8000)")]
    [InlineData("@p IN (1.5, 2.25)", "decimal(38,19)")]
    [InlineData("@p IN (N'a', N'bb')", "nvarchar(4000)")]
    [InlineData("@p IN (c.tier, c.email)", "varchar(8000)")]
    [InlineData("@p IN (1, 3000000000)", "decimal(38,19)")]
    [InlineData("@p BETWEEN 1 AND 2.5", "decimal(38,19)")]
    [InlineData("@p BETWEEN 1 AND 3000000000", "decimal(38,19)")]
    [InlineData("@p BETWEEN c.flags AND c.customer_id", "int")]
    [InlineData("@p BETWEEN c.tier AND c.name", "nvarchar(4000)")]
    public async Task Where_the_operands_of_one_use_give_different_types_haruspex_leaves_the_parameter_unknown(string condition, string type)
    {
        // The server combines the operands' types by rules not observed here, so differing operands decide nothing.
        var query = $"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};";

        Assert.Equal(["@p " + type], (await Described(query)).Parameters);
        Assert.Equal(["@p unknown"], Inferred(query));
    }

    [Theory]
    [InlineData("@p IN ('a', 'b')", "varchar(1)")]
    [InlineData("@p IN (1, 2)", "int")]
    public async Task Where_the_operands_of_one_use_give_one_type_that_type_decides(string condition, string type) =>
        await Both($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};", "@p " + type);

    [Fact]
    public async Task Top_offset_and_fetch_take_bigint() =>
        await Both(
            "SELECT TOP (@n) c.name FROM shop.customers AS c; SELECT c.name FROM shop.customers AS c ORDER BY c.name OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;",
            "@n bigint", "@skip bigint", "@take bigint");

    [Fact]
    public async Task An_argument_takes_the_type_of_the_procedure_s_or_function_s_parameter() =>
        await Both(
            "EXEC shop.touch @customer_id = @c, @why = @w; SELECT shop.discount(@t, @r) AS d; SELECT f.order_id FROM shop.orders_of(@k) AS f;",
            "@c int", "@w nvarchar(20)", "@t decimal(12,2)", "@r decimal(4,3)", "@k bigint");

    [Fact]
    public async Task A_value_inserted_or_set_takes_its_column_s_type() =>
        await Both(
            "INSERT shop.orders (order_id, customer_id, placed, total, qty) VALUES (@o, @c, @p, @t, @q); UPDATE shop.orders SET total = @total WHERE order_id = @id;",
            "@o int", "@c int", "@p datetime2(0)", "@t decimal(12,2)", "@q smallint", "@total decimal(12,2)", "@id int");

    // ---- where the server gives no answer, or one haruspex doesn't claim -----------------------------------------------------

    [Fact]
    public async Task The_server_refuses_a_parameter_used_twice_where_haruspex_decides_from_uses_that_agree()
    {
        const string query = "SELECT c.name FROM shop.customers AS c WHERE c.customer_id = @id OR @id IS NULL;";

        // 11508: the undeclared parameter is used more than once. A file that is a filter of optional arguments needs it.
        Assert.Equal(11508, (await Described(query)).Error);
        Assert.Equal(["@id int"], Inferred(query));
    }

    [Theory]
    [InlineData("SELECT @p AS v;", 11506)]
    [InlineData("SELECT coalesce(@p, 1) AS v;", 11502)]
    [InlineData("SELECT o.placed + @p AS v FROM shop.orders AS o;", 11507)]
    public async Task Where_the_server_can_not_decide_haruspex_leaves_the_parameter_unknown(string query, int error)
    {
        Assert.Equal(error, (await Described(query)).Error);
        Assert.Equal(["@p unknown"], Inferred(query));
    }

    [Fact]
    public async Task Where_the_server_falls_back_on_int_for_a_test_of_null_haruspex_leaves_the_parameter_unknown()
    {
        const string query = "SELECT c.name FROM shop.customers AS c WHERE @p IS NULL;";

        Assert.Equal(["@p int"], (await Described(query)).Parameters);
        Assert.Equal(["@p unknown"], Inferred(query));
    }

    [Fact]
    public async Task A_bit_in_arithmetic_is_typed_datetime_by_the_server_and_left_unknown()
    {
        const string query = "SELECT c.flags + @p AS v, c.active + @q AS w FROM shop.customers AS c;";

        Assert.Equal(["@p tinyint", "@q datetime"], (await Described(query)).Parameters);
        Assert.Equal(["@p tinyint", "@q unknown"], Inferred(query));
    }

    [Theory]
    [InlineData("c.token = @p", "varbinary(16)")]
    [InlineData("c.token > @p", "varbinary(8000)")]
    [InlineData("c.token <= @p", "varbinary(8000)")]
    public async Task A_parameter_compared_with_a_binary_value_takes_its_type_under_equality_and_varbinary_8000_under_ordering(string condition, string type) =>
        await Both($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};", "@p " + type);
}
