using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Whether one document's values fit within another's (<see cref="IrFit"/>): an application built against one database meeting
/// another. Results flow from the database to the application, so they fit when every value the database can return is one
/// the application's schemas accept; parameters flow the other way, so they fit when everything the application can send is
/// one the database's procedure takes. Each case infers the same procedure from two versions of its SQL, as a change to a
/// database would; <c>BaselineFitTests</c>, in the differential tests, calls pairs like them through the old functions on a server.
/// </summary>
public partial class IrFitTests
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
    public void A_trailing_result_set_the_application_may_find_missing_is_ignored_when_it_goes()
    {
        // Its function doesn't count on it, and reads it as [] when it doesn't arrive, which a set that may not run accepts.
        var before = Infer("CREATE PROCEDURE shop.get_order @order_id int AS SELECT 1 AS found; IF @order_id > 0 SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;");
        var after = Infer("CREATE PROCEDURE shop.get_order @order_id int AS SELECT 1 AS found;");

        Assert.Equal(["ignored: shop.get_order results[1]: removed"], Fit(before, after));
    }

    [Fact]
    public void An_output_parameter_s_value_comes_back_so_one_widened_is_refused_as_a_result_is()
    {
        // Declared as the application's type, the procedure's wider value fails the call (8114) or a longer string is cut short.
        var before = Infer("CREATE PROCEDURE shop.count_orders @n int OUTPUT, @s nvarchar(5) OUTPUT AS SELECT @n = 1, @s = N'a';");
        var after = Infer("CREATE PROCEDURE shop.count_orders @n bigint OUTPUT, @s nvarchar(10) OUTPUT AS SELECT @n = 1, @s = N'a';");

        Assert.Equal(
            [
                "refused: shop.count_orders parameters[\"@n\"]: type changed from number (int32) to number (int64)",
                "refused: shop.count_orders parameters[\"@s\"]: length changed from at most 5 to at most 10",
            ],
            Fit(before, after));
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

    /// <summary>The document with the type of the row's field <paramref name="name"/> changed.</summary>
    private static IrDocument WithField(IrDocument document, string name, Func<IrType, IrType> change)
    {
        var (row, type) = document.Types.Single(t => t.Value.Fields.Any(f => f.Name == name));
        var fields = type.Fields.Select(f => f.Name == name ? f with { Type = change(f.Type) } : f);
        return document with { Types = new Dictionary<string, IrObjectType>(document.Types) { [row] = type with { Fields = [.. fields] } } };
    }

    private static IrScalar Faceted(IrType type, Func<IrFacets, IrFacets> change) =>
        Assert.IsType<IrScalar>(type) is var scalar ? scalar with { Facets = change(scalar.Facets ?? new IrFacets()) } : scalar;

    /// <summary>The facets with their length as given, in the unit and encoding they had.</summary>
    private static IrFacets Sized(IrFacets facets, int value, bool isFixed) =>
        facets with { Length = Assert.IsType<IrLength>(facets.Length) with { Value = value, IsFixed = isFixed } };

    private static readonly IrFactSource Declared = new(IrFactSourceKind.Type, "");

    [Fact]
    public void A_key_left_out_when_null_that_may_now_arrive_as_null_is_refused()
    {
        // Observed: the old schema has the key optional and never null, and "note": null fails it.
        const string Cards = "CREATE PROCEDURE shop.list_cards AS SELECT o.order_id, o.note FROM shop.orders AS o FOR JSON PATH;";
        var before = Infer(Cards);
        var after = Infer(Edit(Cards, "FOR JSON PATH", "FOR JSON PATH, INCLUDE_NULL_VALUES"));

        Assert.Equal(["refused: shop.list_cards results[0][].note: presence changed from omittedWhenNull to always"], Fit(before, after));
        Assert.Equal(["refused: shop.list_cards results[0][].note: presence changed from always to omittedWhenNull"], Fit(after, before));
    }

    [Fact]
    public void A_json_result_absent_for_want_of_rows_that_may_now_not_be_sent_is_refused_since_the_function_counts_it()
    {
        // A FOR JSON set with no rows still arrives (decision 0063), so the old function counts it on every call.
        const string Cards = "CREATE PROCEDURE shop.list_cards @show int AS SELECT o.order_id FROM shop.orders AS o FOR JSON PATH;";
        var before = Infer(Cards);
        var after = Infer(Edit(Cards, "AS SELECT", "AS IF @show = 1 SELECT"));

        Assert.Equal(["refused: shop.list_cards results[0]: may now be absent"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Theory]
    [InlineData("datetime2(7)", "smalldatetime", true)]
    [InlineData("datetime2(7)", "datetime2(0)", true)]
    [InlineData("datetime2(0)", "datetime2(7)", false)]
    [InlineData("smalldatetime", "datetime", false)]
    [InlineData("smalldatetime", "datetime2(0)", false)]
    [InlineData("datetime", "smalldatetime", true)]
    [InlineData("datetime", "datetime2(3)", false)]
    [InlineData("datetime", "datetime2(2)", true)]
    [InlineData("datetime2(2)", "datetime", true)] // datetime2 reaches back before 1753
    [InlineData("datetime2(3)", "datetime", true)]
    [InlineData("time(7)", "time(3)", true)]
    [InlineData("time(0)", "time(3)", false)]
    public void A_date_or_time_parameter_fits_when_the_procedure_holds_its_range_and_fraction(string sent, string taken, bool refused)
    {
        // Observed: 2100-01-01 sent to smalldatetime fails the call (8114), and a finer fraction is rounded without an error.
        const string Find = "CREATE PROCEDURE shop.find_orders @at {0} AS SELECT o.order_id FROM shop.orders AS o;";
        var before = Infer(string.Format(System.Globalization.CultureInfo.InvariantCulture, Find, sent));
        var after = Infer(string.Format(System.Globalization.CultureInfo.InvariantCulture, Find, taken));

        Assert.Equal(refused ? [$"refused: shop.find_orders parameters[\"@at\"]: SQL type changed from {sent} to {taken}"] : [], Fit(before, after));
    }

    [Fact]
    public void A_parameter_whose_type_the_procedure_has_as_unknown_is_refused_unless_the_same_unknown_both_sides()
    {
        // Observed: an int sent to a hierarchyid parameter fails the call (206, operand type clash).
        const string Find = "CREATE PROCEDURE shop.find_orders @at {0} AS SELECT o.order_id FROM shop.orders AS o;";
        static IrDocument Taking(string type) => Infer(string.Format(System.Globalization.CultureInfo.InvariantCulture, Find, type));

        Assert.Equal(["refused: shop.find_orders parameters[\"@at\"]: type changed from number (int32) to unknown (unsupported-sql-type)"], Fit(Taking("int"), Taking("hierarchyid")));
        Assert.Empty(Fit(Taking("hierarchyid"), Taking("hierarchyid")));
        Assert.Equal(["refused: shop.find_orders parameters[\"@at\"]: SQL type changed from hierarchyid to geography"], Fit(Taking("hierarchyid"), Taking("geography")));
        Assert.Equal(["refused: shop.find_orders parameters[\"@at\"]: type changed from unknown (unsupported-sql-type) to number (int32)"], Fit(Taking("hierarchyid"), Taking("int")));
    }

    /// <summary>
    /// Types a procedure's parameter may have that the IR names but doesn't model: a schema collection, a table type's columns and
    /// a CLR type are defined in the database, so the same name in both documents may be another type.
    /// </summary>
    private const string Types = """
        GO
        CREATE TYPE shop.lines AS TABLE (a int NOT NULL);
        GO
        CREATE TYPE shop.hierarchyid AS TABLE (a int NOT NULL);
        GO
        CREATE XML SCHEMA COLLECTION shop.sc AS N'<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="a" type="xs:string"/></xs:schema>';
        GO
        CREATE TYPE shop.blob FROM varbinary(16);
        """;

    [Theory]
    [InlineData("hierarchyid", null)]
    [InlineData("geography", null)]
    [InlineData("geometry", null)]
    [InlineData("sql_variant", null)]
    [InlineData("varbinary(16)", null)]
    [InlineData("varbinary(max)", null)]
    [InlineData("binary(4)", null)]
    [InlineData("shop.blob", null)] // an alias type the IR resolves to varbinary(16), which it names
    [InlineData("xml", "xml")] // the IR doesn't say whether it has a schema collection
    [InlineData("xml(shop.sc)", "xml")]
    [InlineData("image", "image")]
    public void A_parameter_of_the_same_unknown_type_both_sides_fits_only_when_its_name_is_all_of_its_definition(string type, string? named)
    {
        // Observed: an xml collection that narrows an element fails the old call (6926) under the same name; a built-in type's
        // name is all there is to it. A table type's definition is in the IR, and judged by it (IrFitTests.TableTypes.cs).
        var document = Infer($"CREATE PROCEDURE shop.find_orders @at {type} AS SELECT o.order_id FROM shop.orders AS o;", Orders + "\n" + Types);

        Assert.Equal(named is null ? [] : [$"refused: shop.find_orders parameters[\"@at\"]: type {named} can't be shown unchanged"], Fit(document, document));
    }

    [Fact]
    public void A_parameter_of_a_user_type_is_named_with_its_schema_so_it_is_not_taken_for_a_built_in_type()
    {
        const string Find = "CREATE PROCEDURE shop.find_orders @at {0} AS SELECT o.order_id FROM shop.orders AS o;";
        static IrDocument Taking(string type) => Infer(string.Format(System.Globalization.CultureInfo.InvariantCulture, Find, type), Orders + "\n" + Types);

        Assert.Equal(["refused: shop.find_orders parameters[\"@at\"]: now a table type, [shop].[hierarchyid] (a int not null)"], Fit(Taking("hierarchyid"), Taking("shop.hierarchyid READONLY")));
        Assert.Empty(Fit(Taking("hierarchyid"), Taking("HierarchyID"))); // a built-in type's name, in any case
    }

    [Fact]
    public void A_decimal_with_more_places_is_refused_and_one_with_fewer_places_and_integer_digits_fits()
    {
        const string Totals = "CREATE PROCEDURE shop.list_totals AS SELECT o.total FROM shop.orders AS o;";
        var before = Infer(Totals);
        var after = Infer(Totals, Edit(Orders, "total decimal(10, 2)", "total decimal(12, 3)"));

        Assert.Contains("refused: shop.list_totals results[0][].total: digits changed from precision 10, scale 2 to precision 12, scale 3", Fit(before, after));
        Assert.DoesNotContain(Fit(after, before), f => f.Contains("digits", StringComparison.Ordinal));
    }

    [Fact]
    public void A_shorter_minimum_length_is_refused()
    {
        var before = WithField(Infer(ListOrders), "customer", t => Faceted(t, f => f with { MinLength = new IrMinLength(2, Declared) }));
        var after = WithField(before, "customer", t => Faceted(t, f => f with { MinLength = new IrMinLength(1, Declared) }));

        Assert.Equal(["refused: shop.list_orders results[0][].customer: minimum length changed from at least 2 to at least 1"], Fit(before, after));
        Assert.Empty(Fit(after, before));
    }

    [Fact]
    public void A_fixed_length_fits_only_the_same_fixed_length()
    {
        var before = WithField(Infer(ListOrders), "customer", t => Faceted(t, f => Sized(f, 10, isFixed: true)));
        var shorter = WithField(before, "customer", t => Faceted(t, f => Sized(f, 8, isFixed: true)));
        var varying = WithField(before, "customer", t => Faceted(t, f => Sized(f, 10, isFixed: false)));

        Assert.Single(Fit(before, shorter), f => f.Contains("length changed", StringComparison.Ordinal));
        Assert.Single(Fit(before, varying), f => f.Contains("length changed", StringComparison.Ordinal));
        Assert.Empty(Fit(before, before));
    }

    [Fact]
    public void A_union_variant_that_fails_its_trial_leaves_no_pair_behind_for_a_later_walk()
    {
        // The row's x accepts one of two objects, and y the first. What arrives in both is an object whose k may be null, which
        // only the second accepts: x fits through it, and y, compared after, must still be refused.
        static IrField Key(bool nullable) => new("k", new IrScalar(IrScalarKind.Number, IrFormat.Int32, "int"), IrPresence.Always, nullable ? [new IrNullReason(IrNullRule.NoRows, "t.sql:1:1")] : []);
        var document = Infer(ListOrders);
        var row = document.Types.Single().Key;
        IrDocument With(params (string Name, IrObjectType Type)[] types) => document with
        {
            Types = new Dictionary<string, IrObjectType>(types.ToDictionary(t => t.Name, t => t.Type)),
        };
        var application = With(
            (row, new IrObjectType([new IrField("x", new IrUnion([new IrRef("Strict"), new IrRef("Loose")]), IrPresence.Always, []), new IrField("y", new IrRef("Strict"), IrPresence.Always, [])])),
            ("Strict", new IrObjectType([Key(nullable: false)])),
            ("Loose", new IrObjectType([Key(nullable: true)])));
        var database = With(
            (row, new IrObjectType([new IrField("x", new IrRef("Given"), IrPresence.Always, []), new IrField("y", new IrRef("Given"), IrPresence.Always, [])])),
            ("Given", new IrObjectType([Key(nullable: true)])));

        Assert.Equal(["refused: shop.list_orders results[0][].y.k: became nullable"], Fit(application, database));
    }
}
