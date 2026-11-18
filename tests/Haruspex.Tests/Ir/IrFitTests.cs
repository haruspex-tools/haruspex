using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Whether one document's values fit within another's (<see cref="IrFit"/>): an application built against one database meeting
/// another. Results flow from the database to the application, so they fit when every value the database can return is one
/// the application's schemas accept; parameters flow the other way, so they fit when everything the application can send is
/// one the database's procedure takes. Each case infers the same procedure from two versions of its SQL, as a change to a
/// database would; <c>BaselineFitTests</c>, in the differential tests, calls pairs like them through the old functions on a server.
/// </summary>
public class IrFitTests
{
    private const string Orders = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            customer nvarchar(50) NOT NULL,
            note nvarchar(200) NULL,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'held')),
            qty tinyint NOT NULL,
            total decimal(10, 2) NOT NULL);
        """;

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS SELECT o.order_id, o.customer, o.status, o.qty FROM shop.orders AS o;";

    private static IrDocument Infer(string procedures, string tables = Orders) =>
        InferProcedure.Infer("shop.*", ("tables.sql", tables), ("procedures.sql", procedures));

    /// <summary>Each finding as <c>refused: …</c> or <c>ignored: …</c>, with the change as <c>haruspex check</c> words it.</summary>
    private static List<string> Fit(IrDocument application, IrDocument database, bool strict = false) =>
        [.. IrFit.Of(application, database, new IrFitOptions { StrictObjects = strict }).Select(f => f.ToString())];

    private static string Edit(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_fits_within_itself()
    {
        var document = Infer(ListOrders);

        Assert.Empty(Fit(document, document));
    }

    [Fact]
    public void A_set_named_renamed_or_unnamed_is_no_finding_since_the_database_sends_sets_by_place()
    {
        // A set's name is the key the application's own function returns it under: the running application keeps its functions,
        // so a name the config file changes reaches it only when it is next built, which check warns of as a return shape
        // (HAR6028), not here.
        var unnamed = Infer(ListOrders);
        IrDocument Named(string name) => unnamed with
        {
            Objects = [.. unnamed.Objects.Select(o => o with { Results = [.. o.Results!.Select(r => r with { Name = name })] })],
        };

        Assert.Empty(Fit(unnamed, Named("orders")));
        Assert.Empty(Fit(Named("orders"), Named("rows")));
        Assert.Empty(Fit(Named("rows"), unnamed));
    }

    [Fact]
    public void A_column_that_becomes_nullable_is_refused_and_one_that_becomes_non_null_fits()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, "customer nvarchar(50) NOT NULL", "customer nvarchar(50) NULL"));

        Assert.Equal(["refused: shop.list_orders results[0][].customer: became nullable"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_new_column_is_ignored_unless_objects_are_strict()
    {
        var before = Infer(ListOrders);
        var after = Infer(Edit(ListOrders, "o.qty", "o.qty, o.note"));

        Assert.Equal(["ignored: shop.list_orders results[0][].note: added (string, nvarchar(200), nullable)"], Fit(before, after));
        Assert.Equal(["refused: shop.list_orders results[0][].note: added (string, nvarchar(200), nullable)"], Fit(before, after, strict: true));
    }

    [Fact]
    public void A_column_removed_is_refused()
    {
        var before = Infer(ListOrders);
        var after = Infer(Edit(ListOrders, "o.customer, ", ""));

        Assert.Equal(["refused: shop.list_orders results[0][].customer: removed (string, nvarchar(50))"], Fit(before, after));
    }

    [Fact]
    public void Columns_in_another_order_fit_since_a_row_is_read_by_name()
    {
        var before = Infer(ListOrders);
        var after = Infer("CREATE PROCEDURE shop.list_orders AS SELECT o.qty, o.status, o.customer, o.order_id FROM shop.orders AS o;");

        Assert.Empty(Fit(before, after));
    }

    [Fact]
    public void A_longer_string_is_refused_and_a_shorter_one_fits()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, "customer nvarchar(50)", "customer nvarchar(80)"));

        Assert.Equal(["refused: shop.list_orders results[0][].customer: length changed from at most 50 to at most 80"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_value_set_that_grows_is_refused_and_one_that_shrinks_fits()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, "('open', 'held')", "('open', 'held', 'closed')"));

        Assert.Equal(["refused: shop.list_orders results[0][].status: value set grew: added \"closed\""], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_value_set_that_goes_is_refused_and_one_that_comes_fits()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, " CHECK (status IN ('open', 'held'))", ""));

        Assert.Equal(["refused: shop.list_orders results[0][].status: no longer drawn from a value set (was \"open\", \"held\")"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_wider_integer_is_refused_and_a_narrower_one_fits_by_the_range_each_allows()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, "qty tinyint", "qty smallint"));

        Assert.Equal(["refused: shop.list_orders results[0][].qty: type changed from number (uint8) to number (int16)"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_bigint_column_is_refused_either_way_since_a_row_delivers_it_as_a_string()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Edit(Orders, "qty tinyint", "qty bigint"));

        Assert.Equal(["refused: shop.list_orders results[0][].qty: type changed from number (uint8) to number (int64)"], Fit(before, after));
        Assert.Equal(["refused: shop.list_orders results[0][].qty: type changed from number (int64) to number (uint8)"], Fit(after, before));
    }

    [Fact]
    public void A_column_of_another_kind_is_refused()
    {
        var before = Infer(ListOrders);
        var after = Infer(Edit(ListOrders, "o.qty", "cast(o.qty AS nvarchar(3)) AS qty"));

        Assert.Equal(["refused: shop.list_orders results[0][].qty: type changed from number (uint8) to string"], Fit(before, after));
    }

    [Fact]
    public void A_new_trailing_result_set_is_ignored_unless_objects_are_strict_and_one_removed_is_refused()
    {
        var before = Infer(ListOrders);
        var after = Infer(Edit(ListOrders, "FROM shop.orders AS o;", "FROM shop.orders AS o; SELECT count(*) AS orders FROM shop.orders;"));

        Assert.Equal(["ignored: shop.list_orders results[1]: added"], Fit(before, after));
        Assert.Equal(["refused: shop.list_orders results[1]: added"], Fit(before, after, strict: true));
        Assert.Equal(["refused: shop.list_orders results[1]: removed"], Fit(after, before));
    }

    [Fact]
    public void A_result_that_may_now_be_absent_is_refused()
    {
        var before = Infer("CREATE PROCEDURE shop.get_order @order_id int AS SELECT 1 AS found; SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;");
        var after = Infer("CREATE PROCEDURE shop.get_order @order_id int AS SELECT 1 AS found; IF @order_id > 0 SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;");

        Assert.Equal(["refused: shop.get_order results[1]: may now be absent"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_key_that_may_now_be_left_out_is_refused()
    {
        var before = Infer("CREATE PROCEDURE shop.get_order AS SELECT o.order_id, o.customer FROM shop.orders AS o FOR JSON PATH;");
        var after = Infer("CREATE PROCEDURE shop.get_order AS SELECT o.order_id, o.customer FROM shop.orders AS o FOR JSON PATH;", Edit(Orders, "customer nvarchar(50) NOT NULL", "customer nvarchar(50) NULL"));

        Assert.Equal(
            [
                "refused: shop.get_order results[0][].customer: became nullable",
                "refused: shop.get_order results[0][].customer: presence changed from always to omittedWhenNull",
            ],
            Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void Results_that_become_unknown_are_refused_and_results_the_application_never_knew_fit_anything()
    {
        var before = Infer(ListOrders);
        var after = Infer("CREATE PROCEDURE shop.list_orders @sql nvarchar(max) = N'' AS EXEC sp_executesql @sql;");

        Assert.Contains("refused: shop.list_orders results: became unknown: the result sets can't be listed", Fit(before, after));
        Assert.DoesNotContain(Fit(after, before), f => f.Contains("results", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_the_application_knows_as_unknown_accepts_anything_and_one_that_becomes_unknown_is_refused()
    {
        var known = Infer("CREATE PROCEDURE shop.get_order AS SELECT o.customer FROM shop.orders AS o;");
        var unknown = Infer("CREATE PROCEDURE shop.get_order AS SELECT o.customer FROM shop.archived_orders AS o;");

        Assert.Single(Fit(known, unknown), f => f.StartsWith("refused: shop.get_order results[0][].customer: became unknown", StringComparison.Ordinal));
        Assert.Empty(Fit(unknown, known));
    }

    [Fact]
    public void A_procedure_the_application_calls_that_is_gone_is_refused_and_a_new_one_is_not_mentioned()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders + "\nGO\nCREATE PROCEDURE shop.count_orders AS SELECT count(*) AS orders FROM shop.orders;");

        Assert.Equal(["refused: shop.count_orders: removed"], Fit(after, before));
        Assert.Empty(Fit(before, after));
    }

    [Fact]
    public void A_shape_that_fits_one_variant_of_a_union_fits_and_a_variant_that_fits_none_is_refused()
    {
        var either = Infer("CREATE PROCEDURE shop.get_row @wide bit AS IF @wide = 1 SELECT o.order_id, o.customer FROM shop.orders AS o; ELSE SELECT o.order_id FROM shop.orders AS o;");
        var narrow = Infer("CREATE PROCEDURE shop.get_row @wide bit AS SELECT o.order_id FROM shop.orders AS o;");
        var other = Infer("CREATE PROCEDURE shop.get_row @wide bit AS SELECT o.status FROM shop.orders AS o;");

        Assert.Empty(Fit(either, narrow));
        Assert.Contains(Fit(either, other), f => f.StartsWith("refused: shop.get_row results[0]: type changed from one of", StringComparison.Ordinal));
    }

    [Fact]
    public void A_nested_object_is_walked_key_by_key_inside_its_json()
    {
        const string Before = "CREATE PROCEDURE shop.get_order AS SELECT o.order_id, (SELECT o.customer FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS who FROM shop.orders AS o FOR JSON PATH;";
        var before = Infer(Before);
        var after = Infer(Edit(Before, "SELECT o.customer FOR", "SELECT o.customer, o.note FOR"));

        Assert.Equal(["ignored: shop.get_order results[0][].who<json>.note: added (string, nvarchar(200), nullable)"], Fit(before, after));
        Assert.Equal(["refused: shop.get_order results[0][].who<json>.note: added (string, nvarchar(200), nullable)"], Fit(before, after, strict: true));
    }

    [Fact]
    public void A_bound_the_database_lacks_is_kept_by_an_exact_set_of_numbers_within_it()
    {
        var bounded = Infer(ListOrders, Edit(Orders, "qty tinyint NOT NULL", "qty tinyint NOT NULL CHECK (qty >= 1 AND qty <= 9)"));
        var listed = Infer(ListOrders, Edit(Orders, "qty tinyint NOT NULL", "qty tinyint NOT NULL CHECK (qty IN (2, 3))"));
        var outside = Infer(ListOrders, Edit(Orders, "qty tinyint NOT NULL", "qty tinyint NOT NULL CHECK (qty IN (2, 30))"));

        Assert.Empty(Fit(bounded, listed));
        Assert.Equal(["refused: shop.list_orders results[0][].qty: upper bound changed from at most 9 to at most 255"], Fit(bounded, outside));
    }

    private const string GetOrder = "CREATE PROCEDURE shop.get_order @order_id int AS SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;";

    [Fact]
    public void A_new_parameter_without_a_default_is_refused_and_one_with_a_default_is_ignored()
    {
        var before = Infer(GetOrder);
        var required = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @channel varchar(10)"));
        var optional = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @channel varchar(10) = 'web'"));

        Assert.Equal(["refused: shop.get_order parameters[\"@channel\"]: added (string, varchar(10))"], Fit(before, required));
        Assert.Equal(["ignored: shop.get_order parameters[\"@channel\"]: added (string, varchar(10))"], Fit(before, optional));
    }

    [Fact]
    public void A_parameter_removed_is_refused_whether_or_not_it_had_a_default_since_the_application_may_send_it()
    {
        var required = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @channel varchar(10)"));
        var optional = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @channel varchar(10) = 'web'"));
        var after = Infer(GetOrder);

        Assert.Equal(["refused: shop.get_order parameters[\"@channel\"]: removed (string, varchar(10))"], Fit(required, after));
        Assert.Equal(["refused: shop.get_order parameters[\"@channel\"]: removed (string, varchar(10))"], Fit(optional, after));
    }

    [Fact]
    public void A_parameter_that_loses_its_default_is_refused_and_one_that_gains_one_fits()
    {
        var before = Infer(GetOrder);
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id int = 0"));

        Assert.Equal(["refused: shop.get_order parameters[\"@order_id\"]: no longer has a default"], Fit(after, before));
        Assert.Empty(Fit(before, after));
    }

    [Fact]
    public void A_narrower_parameter_is_refused_and_a_wider_one_fits_the_other_way_from_results()
    {
        var before = Infer(GetOrder);
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id smallint"));

        Assert.Equal(["refused: shop.get_order parameters[\"@order_id\"]: type changed from number (int32) to number (int16)"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_parameter_that_becomes_bigint_fits_since_the_server_converts_what_is_sent()
    {
        var before = Infer(GetOrder);
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id bigint"));

        Assert.Empty(Fit(before, after));
        Assert.Equal(["refused: shop.get_order parameters[\"@order_id\"]: type changed from number (int64) to number (int32)"], Fit(after, before));
    }

    [Fact]
    public void A_shorter_string_parameter_is_refused_since_the_server_would_cut_what_is_sent()
    {
        var before = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @note nvarchar(40) = N''"));
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @note nvarchar(20) = N''"));

        Assert.Equal(["refused: shop.get_order parameters[\"@note\"]: length changed from at most 40 to at most 20"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_parameter_that_stops_being_output_is_refused_and_one_that_starts_fits()
    {
        var before = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @lines int = NULL OUTPUT"));
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @lines int = NULL"));

        Assert.Equal(["refused: shop.get_order parameters[\"@lines\"]: is no longer an output parameter"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void Each_finding_says_why_in_the_terms_of_the_application_already_running()
    {
        var before = Infer(GetOrder);
        var after = Infer(Edit(GetOrder, "@order_id int", "@order_id int, @channel varchar(10)"));

        var finding = Assert.Single(IrFit.Of(before, after, new IrFitOptions()));

        Assert.Equal(IrFitVerdict.Refused, finding.Verdict);
        Assert.Equal("the application doesn't send it, and it has no default", finding.Why);
    }
}
