using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// The structural comparison of two documents that <c>haruspex check</c> reports: each difference in what the documents
/// claim, with where it is and how it changed, and nothing for what only says where a claim came from (a location, a
/// reason, a hash). Most cases infer the same procedure from two versions of its SQL, as a change to a database would.
/// </summary>
public partial class IrComparisonTests
{
    private const string Orders = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            customer nvarchar(50) NOT NULL,
            note nvarchar(200) NULL,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'held')),
            total decimal(10, 2) NOT NULL);
        """;

    private const string ListOrders = "CREATE PROCEDURE shop.list_orders AS SELECT o.order_id, o.customer, o.status, o.total FROM shop.orders AS o;";

    private static IrDocument Infer(string procedures, string tables = Orders) =>
        InferProcedure.Infer("shop.*", ("tables.sql", tables), ("procedures.sql", procedures));

    /// <summary>Each change as <c>haruspex check</c> words it.</summary>
    private static List<string> Changes(IrDocument before, IrDocument after) => [.. IrComparison.Compare(before, after).Select(c => c.ToString())];

    [Fact]
    public void A_document_compared_with_itself_has_no_changes()
    {
        var document = Infer(ListOrders);

        Assert.Empty(IrComparison.Compare(document, document));
    }

    [Fact]
    public void Moving_a_procedure_down_its_file_changes_only_locations_which_are_not_claims()
    {
        var before = Infer(ListOrders);
        var after = Infer("\n\n-- the orders, for the list page\n" + ListOrders.Replace("AS SELECT", "AS\nSELECT", StringComparison.Ordinal));

        Assert.NotEqual(before, after);
        Assert.Empty(IrComparison.Compare(before, after));
    }

    [Fact]
    public void A_column_that_becomes_nullable_is_reported_where_it_is()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Orders.Replace("customer nvarchar(50) NOT NULL", "customer nvarchar(50) NULL", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders results[0][].customer: became nullable"], Changes(before, after));
    }

    [Fact]
    public void A_column_that_becomes_non_null_is_reported_too_since_the_module_changes()
    {
        var before = Infer(ListOrders.Replace("o.status", "o.note", StringComparison.Ordinal));
        var after = Infer(ListOrders.Replace("o.status", "o.note", StringComparison.Ordinal), Orders.Replace("note nvarchar(200) NULL", "note nvarchar(200) NOT NULL", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders results[0][].note: became non-null"], Changes(before, after));
    }

    [Fact]
    public void A_field_removed_and_a_field_added_are_each_reported_with_its_type()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders.Replace("o.customer", "o.note", StringComparison.Ordinal));

        Assert.Equal(
            [
                "shop.list_orders results[0][].customer: removed (string, nvarchar(50))",
                "shop.list_orders results[0][].note: added (string, nvarchar(200), nullable)",
            ],
            Changes(before, after));
    }

    [Fact]
    public void Fields_in_another_order_are_reported_as_reordered()
    {
        var before = Infer(ListOrders);
        var after = Infer("CREATE PROCEDURE shop.list_orders AS SELECT o.customer, o.order_id, o.status, o.total FROM shop.orders AS o;");

        Assert.Equal(["shop.list_orders results[0][]: fields reordered from order_id, customer, status, total to customer, order_id, status, total"], Changes(before, after));
    }

    [Fact]
    public void A_value_set_that_grows_names_the_values_added()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Orders.Replace("('open', 'held')", "('open', 'held', 'closed')", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders results[0][].status: value set grew: added \"closed\""], Changes(before, after));
    }

    [Fact]
    public void A_value_set_that_shrinks_and_one_that_goes_are_reported()
    {
        var before = Infer(ListOrders);
        var shrunk = Infer(ListOrders, Orders.Replace("('open', 'held')", "('open')", StringComparison.Ordinal));
        var gone = Infer(ListOrders, Orders.Replace(" CHECK (status IN ('open', 'held'))", "", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders results[0][].status: value set shrank: removed \"held\""], Changes(before, shrunk));
        Assert.Equal(["shop.list_orders results[0][].status: no longer drawn from a value set (was \"open\", \"held\")"], Changes(before, gone));
        Assert.Equal(["shop.list_orders results[0][].status: now drawn from a value set (\"open\", \"held\")"], Changes(gone, before));
    }

    [Fact]
    public void A_type_that_changes_kind_is_reported_from_and_to()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders.Replace("o.total", "cast(o.total AS nvarchar(20)) AS total", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders results[0][].total: type changed from number (decimal) to string"], Changes(before, after));
    }

    [Fact]
    public void A_wider_column_is_reported_by_its_sql_type_and_by_the_length_it_claims()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Orders.Replace("customer nvarchar(50)", "customer nvarchar(80)", StringComparison.Ordinal));

        Assert.Equal(
            [
                "shop.list_orders results[0][].customer: SQL type changed from nvarchar(50) to nvarchar(80)",
                "shop.list_orders results[0][].customer: length changed from at most 50 to at most 80",
            ],
            Changes(before, after));
    }

    [Fact]
    public void A_wider_varchar_is_reported_in_bytes_whose_encoding_the_collation_left_unknown()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders, Orders.Replace("status varchar(10)", "status varchar(12)", StringComparison.Ordinal));

        Assert.Equal(
            [
                "shop.list_orders results[0][].status: SQL type changed from varchar(10) to varchar(12)",
                "shop.list_orders results[0][].status: length changed from at most 10 bytes to at most 12 bytes",
            ],
            Changes(before, after));
    }

    [Fact]
    public void A_procedure_added_or_removed_is_reported_by_its_id()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders + "\nGO\nCREATE PROCEDURE shop.count_orders AS SELECT count_big(*) AS orders FROM shop.orders AS o;");

        Assert.Equal(["shop.count_orders: added"], Changes(before, after));
        Assert.Equal(["shop.count_orders: removed"], Changes(after, before));
    }

    [Fact]
    public void A_procedure_whose_generated_name_changes_is_reported_as_renamed()
    {
        // The name is what its function and types are called, so callers change though no claim about a value does.
        var before = Infer(ListOrders);
        var after = before with { Objects = [.. before.Objects.Select(o => o with { Name = "ListAllOrders" })] };

        Assert.Equal(["shop.list_orders: renamed from ListOrders to ListAllOrders"], Changes(before, after));
        Assert.Equal(["shop.list_orders: renamed from ListAllOrders to ListOrders"], Changes(after, before));
    }

    [Fact]
    public void A_query_whose_text_changes_is_reported_though_its_types_do_not()
    {
        // The text is what the query's function sends, so a check must fail when it changes, as when a type does.
        var before = IrSamples.Everything;
        var after = before with { Objects = [.. before.Objects.Select(o => o.Kind == IrObjectKind.Query ? o with { Text = o.Text + "-- reordered\n" } : o)] };

        Assert.Equal(["sql/find_orders.sql: the text it runs changed"], Changes(before, after));
    }

    [Fact]
    public void A_procedure_that_starts_returning_a_code_is_reported()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders.Replace("FROM shop.orders AS o;", "FROM shop.orders AS o; RETURN 3;", StringComparison.Ordinal));

        Assert.Equal(["shop.list_orders: may now end with a return code other than 0"], Changes(before, after));
        Assert.Equal(["shop.list_orders: now always ends with the return code 0"], Changes(after, before));
    }

    [Fact]
    public void A_result_set_named_renamed_or_unnamed_is_reported_since_its_function_returns_it_under_its_name()
    {
        var unnamed = Infer(ListOrders);
        IrDocument Named(string name) => unnamed with
        {
            Objects = [.. unnamed.Objects.Select(o => o with { Results = [.. o.Results!.Select(r => r with { Name = name })] })],
        };

        Assert.Equal(["shop.list_orders results[0]: name changed from none to orders"], Changes(unnamed, Named("orders")));
        Assert.Equal(["shop.list_orders results[0]: name changed from orders to rows"], Changes(Named("orders"), Named("rows")));
        Assert.Equal(["shop.list_orders results[0]: name changed from rows to none"], Changes(Named("rows"), unnamed));
    }

    [Fact]
    public void Results_that_become_unknown_are_reported_as_one_change()
    {
        var before = Infer(ListOrders);
        var after = Infer("CREATE PROCEDURE shop.list_orders @sql nvarchar(max) = N'' AS EXEC sp_executesql @sql;");

        Assert.Equal(
            [
                "shop.list_orders parameters[\"@sql\"]: added (string, nvarchar(max))",
                "shop.list_orders results: became unknown: the result sets can't be listed",
            ],
            Changes(before, after));
        Assert.Contains("shop.list_orders results: became known: 1 result set", Changes(after, before));
    }

    [Fact]
    public void A_result_set_added_and_how_many_rows_a_result_has_are_reported_with_the_rename_a_second_set_brings()
    {
        var before = Infer(ListOrders);
        var after = Infer(ListOrders.Replace("FROM shop.orders AS o;", "FROM shop.orders AS o WHERE o.order_id = 1; SELECT o.note FROM shop.orders AS o;", StringComparison.Ordinal));

        Assert.Equal(
            [
                "shop.list_orders results[0]: rows changed from many to atMostOne",
                "shop.list_orders results[0][]: type renamed from ListOrdersRow to ListOrdersResult1Row",
                "shop.list_orders results[1]: added",
            ],
            Changes(before, after));
    }

    [Fact]
    public void A_parameter_whose_type_changes_is_reported_by_its_name()
    {
        var before = Infer("CREATE PROCEDURE shop.get_order @order_id int AS SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;");
        var after = Infer("CREATE PROCEDURE shop.get_order @order_id bigint = NULL AS SELECT o.customer FROM shop.orders AS o WHERE o.order_id = @order_id;");

        Assert.Equal(
            [
                "shop.get_order parameters[\"@order_id\"]: type changed from number (int32) to number (int64)",
                "shop.get_order parameters[\"@order_id\"]: SQL type changed from int to bigint",

                // A parameter keeps its type's range, which a client checks a value against before sending it.
                "shop.get_order parameters[\"@order_id\"]: lower bound changed from at least -2147483648 to at least -9223372036854775808",
                "shop.get_order parameters[\"@order_id\"]: upper bound changed from at most 2147483647 to at most 9223372036854775807",
                "shop.get_order parameters[\"@order_id\"]: now has a default",
            ],
            Changes(before, after));
    }

    [Fact]
    public void A_renamed_type_is_reported_and_its_fields_are_still_compared()
    {
        var before = Hand(new() { ["OrderRow"] = Row(Field("id", Int)) }, "OrderRow");
        var after = Hand(new() { ["ListOrdersRow"] = Row(Field("id", Int, nullable: true)) }, "ListOrdersRow");

        Assert.Equal(
            [
                "shop.list_orders results[0][]: type renamed from OrderRow to ListOrdersRow",
                "shop.list_orders results[0][].id: became nullable",
            ],
            Changes(before, after));
    }

    [Fact]
    public void A_recursive_type_is_compared_once_and_the_walk_ends()
    {
        // Node holds an array of Node: following the reference again would never end.
        var before = Hand(new() { ["Node"] = Row(Field("id", Int), Field("children", new IrArray(new IrRef("Node")))) }, "Node");
        var after = Hand(new() { ["Node"] = Row(Field("id", Int, nullable: true), Field("children", new IrArray(new IrRef("Node")))) }, "Node");

        Assert.Equal(["shop.list_orders results[0][].id: became nullable"], Changes(before, after));
    }

    [Fact]
    public void Reasons_and_their_locations_are_not_claims_only_whether_there_is_one()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int, nullable: true)) }, "Row");
        var after = Hand(new() { ["Row"] = Row(new IrField("id", Int, IrPresence.Always, [new IrNullReason(IrNullRule.UnmatchedRow, "q.sql:9:9"), new IrNullReason(IrNullRule.NullableColumn, "t.sql:2:2")])) }, "Row");

        Assert.Empty(IrComparison.Compare(before, after));
    }

    [Fact]
    public void A_key_left_out_when_null_is_reported_as_a_change_of_presence()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int, nullable: true)) }, "Row");
        var after = Hand(new() { ["Row"] = Row(Field("id", Int, nullable: true) with { Presence = IrPresence.OmittedWhenNull }) }, "Row");

        Assert.Equal(["shop.list_orders results[0][].id: presence changed from always to omittedWhenNull"], Changes(before, after));
    }

    [Fact]
    public void A_field_that_becomes_unknown_says_what_it_was_and_why()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        var after = Hand(new() { ["Row"] = Row(Field("id", new IrUnknownType(IrUnknownReason.UnsupportedExpression))) }, "Row");

        Assert.Equal(["shop.list_orders results[0][].id: became unknown (unsupported-expression); it was number (int32)"], Changes(before, after));
        Assert.Equal(["shop.list_orders results[0][].id: became known: number (int32)"], Changes(after, before));
    }

    [Fact]
    public void A_union_is_compared_variant_by_variant_as_claims_at_its_own_path()
    {
        IrScalar Code(int length, string from) => new(IrScalarKind.String, null, "varchar", new IrFacets(Length: new IrLength(length, false, IrLengthUnit.Utf16, IrByteEncoding.Utf8, new IrFactSource(IrFactSourceKind.Type, from))));
        IrDocument Union(int length, string from) => Hand(new() { ["Row"] = Row(Field("code", new IrUnion([Code(length, from), Int]))) }, "Row");

        Assert.Empty(IrComparison.Compare(Union(10, "shop.orders.code"), Union(10, "shop.returns.code")));
        Assert.Equal(["shop.list_orders results[0][].code: length changed from at most 10 to at most 12"], Changes(Union(10, "a"), Union(12, "a")));
        Assert.Equal(
            ["shop.list_orders results[0][].code: type changed from one of string, number (int32) to one of string, number (int32), boolean"],
            Changes(Union(10, "a"), Hand(new() { ["Row"] = Row(Field("code", new IrUnion([Code(10, "a"), Int, new IrScalar(IrScalarKind.Boolean, null, "bit")]))) }, "Row")));
    }

    [Fact]
    public void The_declared_session_is_a_claim_of_the_whole_document()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        var after = before with { Source = before.Source with { Session = [new IrSessionOption("ansiWarnings", true)] } };

        Assert.Equal(["source.session: changed from none declared to ansiWarnings on"], Changes(before, after));
    }

    [Fact]
    public void A_package_declared_a_build_or_no_longer_is_a_change_of_the_whole_document_naming_it()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        IrDocument Declaring(params string[] builds) => before with { Source = before.Source with { Builds = [.. builds] } };

        Assert.Equal(["source.builds[\"sales.dacpac\"]: now declared a build"], Changes(Declaring("ledger.dacpac"), Declaring("ledger.dacpac", "sales.dacpac")));
        Assert.Equal(["source.builds[\"ledger.dacpac\"]: no longer declared a build"], Changes(Declaring("ledger.dacpac", "sales.dacpac"), Declaring("sales.dacpac")));
        Assert.Empty(IrComparison.Compare(Declaring("sales.dacpac"), Declaring("sales.dacpac")));
    }

    [Fact]
    public void A_change_of_declared_build_says_both_what_it_was_and_what_it_is()
    {
        // The JSON report writes before and after; one of them null would say the package had no state before.
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        var after = before with { Source = before.Source with { Builds = ["sales.dacpac"] } };

        var change = Assert.Single(IrComparison.Compare(before, after));
        Assert.Equal(("false", "true"), (change.Before, change.After));
        var back = Assert.Single(IrComparison.Compare(after, before));
        Assert.Equal(("true", "false"), (back.Before, back.After));
    }

    [Fact]
    public void A_baseline_s_package_declared_a_build_is_a_change_named_at_the_baseline()
    {
        var before = IrSamples.Built;
        var after = before with { Source = before.Source with { Baseline = before.Source.Baseline! with { Builds = [] } } };

        Assert.Equal(["source.baseline.builds[\"shop-release.dacpac\"]: no longer declared a build"], Changes(before, after));
    }

    [Fact]
    public void A_folder_added_to_or_removed_from_a_source_of_several_is_a_change_naming_it()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        IrDocument Folders(params string[] folders) => before with { Source = before.Source with { Paths = [.. folders] } };
        IrDocument Folder(string folder) => before with { Source = before.Source with { Path = folder } };

        Assert.Equal(["source.paths.views: now read"], Changes(Folders("procedures", "tables"), Folders("procedures", "tables", "views")));
        Assert.Equal(["source.paths.procedures: no longer read", "source.paths.views: now read"], Changes(Folders("procedures", "tables"), Folders("tables", "views")));
        Assert.Equal(["source.paths.tables: now read"], Changes(Folder("procedures"), Folders("procedures", "tables")));
        Assert.Equal(["source.paths.tables: no longer read"], Changes(Folders("procedures", "tables"), Folder("procedures")));
        Assert.Empty(IrComparison.Compare(Folders("procedures", "tables"), Folders("tables", "procedures")));
    }

    [Fact]
    public void Two_sources_each_of_one_folder_compare_no_paths()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");

        Assert.Empty(IrComparison.Compare(before with { Source = before.Source with { Path = "database" } }, before with { Source = before.Source with { Path = "db" } }));
    }

    [Fact]
    public void A_baseline_s_folder_removed_is_a_change_named_at_the_baseline()
    {
        var before = IrSamples.Folders;
        var after = before with { Source = before.Source with { Baseline = before.Source.Baseline! with { Paths = ["release/procedures", "release/views"] } } };

        Assert.Equal(["source.baseline.paths[\"release/tables\"]: no longer read", "source.baseline.paths[\"release/views\"]: now read"], Changes(before, after));
    }

    [Fact]
    public void A_different_package_or_its_hash_is_not_a_claim()
    {
        var before = Hand(new() { ["Row"] = Row(Field("id", Int)) }, "Row");
        var after = before with { Source = before.Source with { Package = new IrPackage("shop.dacpac", new string('0', 64)) } };

        Assert.Empty(IrComparison.Compare(before, after));
    }

    public static TheoryData<string> Snapshots { get; } = [.. Fixture.All.SelectMany(f => f.Snapshots).Select(s => Path.GetRelativePath(RepositoryPaths.Fixtures, s.Ir))];

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void Every_fixture_s_ir_has_no_changes_from_itself_with_every_location_moved(string snapshot)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryPaths.Fixtures, snapshot));
        var moved = System.Text.RegularExpressions.Regex.Replace(
            text, "(\\.sql):(\\d+):", m => $"{m.Groups[1].Value}:{int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + 7}:");
        var document = IrJsonReader.Read(text);

        Assert.NotEqual(text, moved);
        Assert.Empty(IrComparison.Compare(document, IrJsonReader.Read(moved)));
    }

    [Theory]
    [MemberData(nameof(Snapshots))]
    public void Every_fixture_s_procedures_are_each_one_change_when_they_are_all_gone(string snapshot)
    {
        var document = IrJsonReader.Read(File.ReadAllText(Path.Combine(RepositoryPaths.Fixtures, snapshot)));
        var empty = document with { Objects = [], Types = new Dictionary<string, IrObjectType>(), Unknowns = [] };

        Assert.Equal(document.Objects.Select(o => $"{o.Id}: removed").Order(StringComparer.Ordinal), Changes(document, empty));
    }

    private static readonly IrScalar Int = new(IrScalarKind.Number, IrFormat.Int32, "int");

    private static IrField Field(string name, IrType type, bool nullable = false) =>
        new(name, type, IrPresence.Always, nullable ? [new IrNullReason(IrNullRule.NullableColumn, "t.sql:1:1")] : []);

    private static IrObjectType Row(params IrField[] fields) => new([.. fields]);

    /// <summary>A document built by hand: <c>shop.list_orders</c> returning rows of the type named <paramref name="row"/>.</summary>
    private static IrDocument Hand(Dictionary<string, IrObjectType> types, string row) => new(
        new IrSource("tsql"),
        [new IrObject("shop.list_orders", "ListOrders", IrObjectKind.Procedure, "p.sql:1:1", [], [new IrResult(IrEncoding.Rowset, new IrArray(new IrRef(row)), [])])],
        types,
        []);
}
