using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// Query files: a batch of the application's, typed as a procedure's body is, whose parameters are the variables it uses without
/// declaring, each typed by its declaration comment or else by its uses, by the rule SQL Server's own
/// <c>sp_describe_undeclared_parameters</c> follows (<c>QueryParameterObservationTests</c> observes each). Reading the file is in
/// <c>QueryFileTests.Reading.cs</c>.
/// </summary>
public partial class QueryFileTests
{
    private const string Shop = InferQuery.Shop;

    private static IReadOnlyList<string> Parameters(string query) => InferQuery.Infer(Shop, query).Parameters();

    /// <summary>The unknown of the query's one undecided parameter, under its parameters; a result its value reaches is unknown too.</summary>
    private static IrUnknown Unknown(IrDocument document) =>
        Assert.Single(document.Unknowns, u => u.Reason == IrUnknownReason.UndecidedParameter && u.Path.StartsWith("parameters.", StringComparison.Ordinal));

    [Fact]
    public void A_variable_compared_with_a_column_is_a_parameter_of_the_column_s_type() =>
        Assert.Equal(["@id int"], Parameters("SELECT c.name FROM shop.customers AS c WHERE c.customer_id = @id;"));

    [Fact]
    public void Equality_takes_a_string_column_s_kind_and_length() =>
        Assert.Equal(
            ["@name nvarchar(80)", "@email varchar(120)", "@tier char(1)", "@code nchar(4)", "@notes nvarchar(max)"],
            Parameters("SELECT 1 AS one FROM shop.customers AS c WHERE c.name = @name AND c.email = @email AND c.tier = @tier AND c.code = @code AND c.notes = @notes;"));

    [Fact]
    public void Equality_takes_a_decimal_s_digits_and_a_time_s_places_on_either_side() =>
        Assert.Equal(
            ["@balance decimal(10,2)", "@seen datetime2(3)"],
            Parameters("SELECT 1 AS one FROM shop.customers AS c WHERE @balance = c.balance AND c.seen <> @seen;"));

    [Fact]
    public void Ordering_widens_a_decimal_a_string_and_a_time_but_keeps_an_integer_and_a_date() =>
        Assert.Equal(
            ["@balance decimal(38,19)", "@name nvarchar(4000)", "@email varchar(8000)", "@seen datetime2(7)", "@id int", "@joined date", "@notes nvarchar(max)"],
            Parameters("""
                SELECT 1 AS one FROM shop.customers AS c
                WHERE c.balance > @balance AND c.name >= @name AND @email < c.email AND c.seen <= @seen AND c.customer_id > @id AND c.joined > @joined
                  AND c.notes > @notes;
                """));

    [Fact]
    public void Like_takes_the_longest_string_of_the_column_s_kind() =>
        Assert.Equal(
            ["@name nvarchar(4000)", "@email varchar(8000)", "@pattern nvarchar(4000)"],
            Parameters("SELECT 1 AS one FROM shop.customers AS c WHERE c.name LIKE @name AND c.email LIKE @email AND c.notes LIKE @pattern;"));

    [Fact]
    public void In_lists_and_between_take_the_column_s_type_by_their_kind_of_comparison() =>
        Assert.Equal(
            ["@a int", "@b int", "@from date", "@to date", "@low decimal(38,19)"],
            Parameters("SELECT 1 AS one FROM shop.customers AS c WHERE c.customer_id IN (@a, @b) AND c.joined BETWEEN @from AND @to AND c.balance BETWEEN @low AND 100;"));

    [Fact]
    public void A_parameter_compared_with_a_literal_takes_the_literal_s_type() =>
        Assert.Equal(["@n int", "@s varchar(3)", "@u nvarchar(1)"], Parameters("IF @n = 1 AND @s = 'abc' AND @u = N'x' SELECT 1 AS one;"));

    [Fact]
    public void Arithmetic_in_a_select_list_takes_the_operand_s_type_widening_a_decimal_and_a_string() =>
        Assert.Equal(
            ["@a smallint", "@b decimal(38,19)", "@c nvarchar(4000)", "@d float", "@e money", "@f int"],
            Parameters("SELECT o.qty - @a AS a, o.total * @b AS b, c.name + @c AS c, c.rate / @d AS d, c.credit + @e AS e, @f + 1 AS f FROM shop.orders AS o JOIN shop.customers AS c ON c.customer_id = o.customer_id;"));

    [Fact]
    public void Arithmetic_inside_a_comparison_decides_nothing_since_the_server_types_it_from_the_comparison()
    {
        var document = InferQuery.Infer(Shop, "SELECT 1 AS one FROM shop.orders AS o WHERE o.total = @x * 2;");

        Assert.Equal(["@x unknown"], document.Parameters());
        Assert.Equal("parameters.@x", Unknown(document).Path);
    }

    [Fact]
    public void Top_percent_takes_float() =>
        Assert.Equal(["@p float"], Parameters("SELECT TOP (@p) PERCENT c.name FROM shop.customers AS c;"));

    [Theory]
    [InlineData("@p IN ('a', 'bb')")]
    [InlineData("@p BETWEEN 1 AND 2.5")]
    [InlineData("@p BETWEEN c.flags AND c.customer_id")]
    public void Operands_of_one_use_that_give_different_types_leave_the_parameter_unknown(string condition) =>
        Assert.Equal(["@p unknown"], Parameters($"SELECT 1 AS one FROM shop.customers AS c WHERE {condition};"));

    [Fact]
    public void Top_offset_and_fetch_take_bigint() =>
        Assert.Equal(
            ["@n bigint", "@skip bigint", "@take bigint"],
            Parameters("SELECT TOP (@n) c.name FROM shop.customers AS c; SELECT c.name FROM shop.customers AS c ORDER BY c.name OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;"));

    [Fact]
    public void An_argument_of_a_procedure_or_a_function_takes_its_parameter_s_type() =>
        Assert.Equal(
            ["@c int", "@w nvarchar(20)", "@t decimal(12,2)", "@r decimal(4,3)", "@k bigint"],
            Parameters("EXEC shop.touch @customer_id = @c, @why = @w; SELECT shop.discount(@t, @r) AS d; SELECT f.order_id FROM shop.orders_of(@k) AS f;"));

    [Fact]
    public void A_positional_argument_of_a_procedure_takes_the_parameter_in_its_place() =>
        Assert.Equal(["@c int", "@w nvarchar(20)"], Parameters("EXEC shop.touch @c, @w;"));

    [Fact]
    public void A_value_inserted_into_or_set_on_a_column_takes_the_column_s_type() =>
        Assert.Equal(
            ["@o int", "@c int", "@p datetime2(0)", "@t decimal(12,2)", "@q smallint", "@total decimal(12,2)", "@id int"],
            Parameters("""
                INSERT shop.orders (order_id, customer_id, placed, total, qty) VALUES (@o, @c, @p, @t, @q);
                UPDATE shop.orders SET total = @total WHERE order_id = @id;
                """));

    [Fact]
    public void A_delete_s_where_types_its_parameters() =>
        Assert.Equal(["@id int"], Parameters("DELETE FROM shop.orders WHERE order_id = @id;"));

    [Fact]
    public void A_column_of_a_temporary_table_or_a_cte_types_a_parameter_compared_with_it() =>
        Assert.Equal(
            ["@k smallint", "@n nvarchar(80)"],
            Parameters("""
                CREATE TABLE #picked (k smallint NOT NULL);
                INSERT #picked (k) SELECT o.qty FROM shop.orders AS o;
                SELECT p.k FROM #picked AS p WHERE p.k = @k;
                WITH named AS (SELECT c.name FROM shop.customers AS c) SELECT n.name FROM named AS n WHERE n.name = @n;
                """));

    [Fact]
    public void Uses_that_agree_decide_the_type_even_where_the_server_refuses_more_than_one() =>
        Assert.Equal(["@id int"], Parameters("SELECT c.name FROM shop.customers AS c WHERE c.customer_id = @id OR @id IS NULL; SELECT o.order_id FROM shop.orders AS o WHERE o.customer_id = @id;"));

    [Fact]
    public void Uses_that_disagree_leave_the_parameter_unknown_naming_each()
    {
        var document = InferQuery.Infer(Shop, "SELECT c.name FROM shop.customers AS c WHERE c.name = @who OR c.email = @who;");

        Assert.Equal(["@who unknown"], document.Parameters());
        Assert.Equal(
            "The uses of @who decide different types: nvarchar(80) where it is compared with c.name (queries/q.sql:1:55); varchar(120) where it is compared with c.email (queries/q.sql:1:73). Declare it in the file: -- @param @who <type>.",
            Unknown(document).Detail);
    }

    [Fact]
    public void A_parameter_nothing_decides_is_unknown_and_says_how_to_declare_it()
    {
        var document = InferQuery.Infer(Shop, "SELECT @label AS label;");

        var unknown = Unknown(document);
        Assert.Equal(("queries/q.sql", "parameters.@label", "queries/q.sql:1:8"), (unknown.Object, unknown.Path, unknown.At));
        Assert.EndsWith("Declare it in the file: -- @param @label <type>.", unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(new IrUnknownType(IrUnknownReason.UndecidedParameter), document.Query().Parameters[0].Type);
    }

    [Fact]
    public void A_declaration_comment_decides_the_type_over_what_the_uses_say() =>
        Assert.Equal(
            ["@label nvarchar(30)", "@who varchar(120)"],
            Parameters("""
                -- @param @who varchar(120)
                -- @param @label nvarchar(30)
                SELECT @label AS label FROM shop.customers AS c WHERE c.name = @who OR c.email = @who;
                """));

    [Fact]
    // Spelt as written: two spellings of one name are an error (HAR1022, decision 0119).
    public void Parameters_come_in_the_order_the_text_first_uses_them_spelt_as_written() =>
        Assert.Equal(
            ["@b int", "@A date"],
            Parameters("SELECT 1 AS one FROM shop.customers AS c WHERE c.customer_id = @b AND c.joined = @A AND c.joined > @A;"));

    [Fact]
    public void A_variable_the_file_declares_is_its_own_not_a_parameter() =>
        Assert.Equal(
            ["@id int"],
            Parameters("DECLARE @limit int = 10; SELECT TOP (@limit) c.name FROM shop.customers AS c WHERE c.customer_id > @id;"));

    [Fact]
    public void Results_are_typed_with_the_parameters_types_and_a_parameter_may_be_null()
    {
        var document = InferQuery.Infer(Shop, "-- @param @label nvarchar(30)\nSELECT @label AS label;");

        var row = Assert.IsType<IrRef>(Assert.IsType<IrArray>(document.Query().Results![0].Type).Items);
        var label = Assert.Single(document.Types[row.Name].Fields);
        Assert.Equal("nvarchar(30)", Assert.IsType<IrScalar>(label.Type).Sql);
        Assert.Equal([IrNullRule.Parameter], label.NullableBecause.Select(r => r.Rule));
    }

    [Fact]
    public void A_query_s_object_is_its_path_with_the_text_it_runs()
    {
        const string text = "-- Orders of a customer.\nSELECT o.order_id FROM shop.orders AS o WHERE o.customer_id = @customer_id;\n";

        var query = InferQuery.Infer(Shop, text, "sql/orders/by_customer.sql").Query();

        Assert.Equal(("sql/orders/by_customer.sql", "sql/orders/by_customer.sql", "sql/orders/by_customer.sql:1:1", text), (query.Id, query.Name, query.At, query.Text));
        Assert.Equal([new IrParameter("@customer_id", query.Parameters[0].Type, HasDefault: false, IsOutput: false)], query.Parameters);
    }
}
