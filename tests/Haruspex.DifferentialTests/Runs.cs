namespace Haruspex.DifferentialTests;

/// <summary>One call of a procedure, against a fixture's seeded database.</summary>
/// <param name="Name">A unique name for the run, shown in the test's name.</param>
/// <param name="Fixture">The folder under <c>tests/fixtures</c>.</param>
/// <param name="Procedure">The procedure's id.</param>
/// <param name="Parameters">The arguments, by parameter name.</param>
/// <param name="Setup">
/// SQL run first, in a transaction rolled back afterwards: a way to see the tables in a state the seed does not
/// leave them in, such as empty, so that a procedure without parameters can still be run with no rows. A run with a
/// setup writes, so it runs alone in the database (a write session); every other run must only read, and fails if
/// its procedure writes (README.md, "Runs that write").
/// </param>
public sealed record ProcedureRun(
    string Name,
    string Fixture,
    string Procedure,
    IReadOnlyDictionary<string, object?> Parameters,
    string? Setup = null)
{
    public override string ToString() => Name;
}

/// <summary>
/// Every procedure run of the differential tests. Each fixture procedure is run here or listed in
/// <see cref="NotRun"/> with the reason; a test holds the two lists to that.
/// </summary>
public static class Runs
{
    /// <summary>The fixtures deployed into the test database. Their schemas must not overlap.</summary>
    public static IReadOnlyList<string> Fixtures { get; } = ["orders", "values", "checks", "payloads", "trees", "flow", "calls", "refine", "dml", "apply", "tabs", "cases", "sets"];

    /// <summary>
    /// The fixtures whose seed must reach every edge the IR claims (Coverage.MissingEdges): each bound, each value
    /// of a set, each full length. They are the fixtures about scalar facts. <c>orders</c> is about query shapes,
    /// and its scalars are the same types <c>values</c> covers at their edges; seeding its ids and totals at
    /// int.MinValue would only obscure what it shows. <c>payloads</c> is here because it shows that facts pass through the
    /// JSON constructors, and that an array's length bounds hold. <c>trees</c> is here because facts pass through functions
    /// and recursive types too, where an edge counts at any depth of the tree.
    /// </summary>
    public static IReadOnlyList<string> EdgeFixtures { get; } = ["values", "checks", "payloads", "trees"];

    private const string NoOrders = "DELETE FROM sales.order_line; DELETE FROM sales.[order]; DELETE FROM sales.customer;";
    private const string NoSamples = "DELETE FROM vals.sample_tag; DELETE FROM vals.sample;";
    private const string NoTickets = "DELETE FROM chk.ticket_event; DELETE FROM chk.ticket;";
    private const string NoBooks = "DELETE FROM pay.book_tag; DELETE FROM pay.review; DELETE FROM pay.book;";
    private const string NoReviews = "DELETE FROM pay.review;";
    private const string NoParts = "DELETE FROM bom.component; DELETE FROM bom.part;";
    private const string NoItems = "DELETE FROM cat.item;";
    private const string NoSales = "DELETE FROM flow.sale;";
    private const string NoEmployees = "DELETE FROM flow.employee;";
    private const string NoRegions = "DELETE FROM flow.region;";
    private const string NoBakery = "DELETE FROM calls.delivery; DELETE FROM calls.batch; DELETE FROM calls.oven;";
    private const string NoLibrary = "DELETE FROM lend.loan; DELETE FROM lend.copy; DELETE FROM lend.title; DELETE FROM lend.member;";
    private const string NoLoans = "DELETE FROM lend.loan;";
    private const string NoMarket = "DELETE FROM mart.purchase_line; DELETE FROM mart.purchase; DELETE FROM mart.price; DELETE FROM mart.product; DELETE FROM mart.customer;";
    private const string NoPurchases = "DELETE FROM mart.purchase_line; DELETE FROM mart.purchase;";
    private const string NoOpenTabs = "UPDATE cafe.tab SET closed_at = DATEADD(hour, 1, opened_at) WHERE closed_at IS NULL;";
    private const string NoCafe = "DELETE FROM cafe.tab_item; DELETE FROM cafe.tab; DELETE FROM cafe.menu_item; DELETE FROM cafe.guest;";
    private const string NoDesk = "DELETE FROM desk.part_line; DELETE FROM desk.job_note; DELETE FROM desk.job; DELETE FROM desk.customer;";
    private const string NoRink = "DELETE FROM rink.booking; DELETE FROM rink.locker; DELETE FROM rink.session; DELETE FROM rink.guest; DELETE FROM rink.member;";

    private static readonly Dictionary<string, object?> None = [];

    // Runs of procedures that write permanent tables need a write session, which a setup asks for; this one changes nothing.
    private const string Writes = "SET NOCOUNT ON;";
    private const string NothingLow = "UPDATE depot.stock SET qty = 100;";
    private const string ShelvedLow = "UPDATE depot.stock SET qty = 1 WHERE part_id = 2;";

    public static IReadOnlyList<ProcedureRun> All { get; } =
    [
        new("orders: list_customers", "orders", "sales.list_customers", None),
        new("orders: list_orders, every order", "orders", "sales.list_orders", None),
        new("orders: list_orders, a customer with no orders", "orders", "sales.list_orders", new Dictionary<string, object?> { ["@customer_id"] = 3 }),
        new("orders: get_order, with lines and a salesperson", "orders", "sales.get_order", new Dictionary<string, object?> { ["@order_id"] = 100 }),
        new("orders: get_order, no lines, no salesperson, nulls", "orders", "sales.get_order", new Dictionary<string, object?> { ["@order_id"] = 101 }),
        new("orders: get_order, a customer with no email", "orders", "sales.get_order", new Dictionary<string, object?> { ["@order_id"] = 102 }),
        new("orders: get_order, no such order", "orders", "sales.get_order", new Dictionary<string, object?> { ["@order_id"] = 999 }),
        new("orders: list_customer_orders", "orders", "sales.list_customer_orders", None),
        new("orders: list_customer_orders, no customers", "orders", "sales.list_customer_orders", None, NoOrders),
        new("orders: archive_orders", "orders", "sales.archive_orders", new Dictionary<string, object?> { ["@before"] = new DateTime(2026, 12, 31) }),

        new("values: list_samples", "values", "vals.list_samples", None),
        new("values: list_samples, no match", "values", "vals.list_samples", new Dictionary<string, object?> { ["@sample_id"] = -1 }),
        new("values: list_samples_with_nulls", "values", "vals.list_samples_with_nulls", None),
        new("values: list_samples_with_nulls, no samples", "values", "vals.list_samples_with_nulls", None, NoSamples),
        new("values: get_sample, typical", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = 1 }),
        new("values: get_sample, nulls", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = 5 }),
        new("values: get_sample, no such sample", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = -1 }),
        new("values: get_sample, maxima", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = 2 }),
        new("values: get_sample, minima", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = 3 }),
        new("values: get_sample, lowest key", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = int.MinValue }),
        new("values: get_sample, highest key", "values", "vals.get_sample", new Dictionary<string, object?> { ["@sample_id"] = int.MaxValue }),
        new("values: sample_rows", "values", "vals.sample_rows", None),
        new("values: list_nested", "values", "vals.list_nested", None),
        new("values: list_nested, no samples", "values", "vals.list_nested", None, NoSamples),
        new("values: list_nested_with_nulls", "values", "vals.list_nested_with_nulls", None),
        new("values: list_nested_with_nulls, no samples", "values", "vals.list_nested_with_nulls", None, NoSamples),

        new("checks: list_tickets", "checks", "chk.list_tickets", None),
        new("checks: list_tickets, no tickets", "checks", "chk.list_tickets", None, NoTickets),
        new("checks: ticket_rows", "checks", "chk.ticket_rows", None),

        new("payloads: get_author, the longest values and both extreme books", "payloads", "pay.get_author", new Dictionary<string, object?> { ["@author_id"] = int.MinValue }),
        new("payloads: get_author, one book, nulls", "payloads", "pay.get_author", new Dictionary<string, object?> { ["@author_id"] = int.MaxValue }),
        new("payloads: get_author, no books", "payloads", "pay.get_author", new Dictionary<string, object?> { ["@author_id"] = 3 }),
        new("payloads: get_author, a book with no reviews", "payloads", "pay.get_author", new Dictionary<string, object?> { ["@author_id"] = 4 }),
        new("payloads: get_author, no such author", "payloads", "pay.get_author", new Dictionary<string, object?> { ["@author_id"] = 999 }),
        new("payloads: list_books", "payloads", "pay.list_books", None),
        new("payloads: list_books, no books", "payloads", "pay.list_books", None, NoBooks),
        new("payloads: author_stats", "payloads", "pay.author_stats", None),
        new("payloads: author_stats, no books", "payloads", "pay.author_stats", None, NoBooks),
        new("payloads: reviews_by_book", "payloads", "pay.reviews_by_book", None),
        new("payloads: reviews_by_book, no reviews", "payloads", "pay.reviews_by_book", None, NoReviews),
        new("payloads: author_documents", "payloads", "pay.author_documents", None),
        new("payloads: review_digest", "payloads", "pay.review_digest", None),
        new("payloads: review_digest, no reviews", "payloads", "pay.review_digest", None, NoReviews),
        // The text's content claims at least one id when it is not null: one review is that edge.
        new("payloads: review_digest, one review", "payloads", "pay.review_digest", None, "DELETE FROM pay.review WHERE review_id <> 7;"),

        new("trees: get_assembly, four levels deep", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = 1 }),
        new("trees: get_assembly, a part with no components, lowest key", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = int.MinValue }),
        new("trees: get_assembly, a part used in three assemblies, highest key", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = int.MaxValue }),
        new("trees: get_assembly, an assembly with no components", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = 12 }),
        new("trees: get_assembly, an assembly of one component", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = 11 }),
        new("trees: get_assembly, the longest name", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = 21 }),
        new("trees: get_assembly, no such part", "trees", "bom.get_assembly", new Dictionary<string, object?> { ["@part_id"] = 999 }),
        new("trees: list_parts", "trees", "bom.list_parts", None),
        new("trees: list_parts, no parts", "trees", "bom.list_parts", None, NoParts),
        new("trees: get_category, the whole catalogue", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = 5 }),
        new("trees: get_category, lowest key", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = int.MinValue }),
        new("trees: get_category, a leaf, highest key", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = int.MaxValue }),
        new("trees: get_category, the longest name", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = 3 }),
        new("trees: get_category, empty", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = 4 }),
        new("trees: get_category, no such category", "trees", "cat.get_category", new Dictionary<string, object?> { ["@category_id"] = 999 }),
        new("trees: list_items", "trees", "cat.list_items", None),
        new("trees: list_items, no items", "trees", "cat.list_items", None, NoItems),
        new("trees: item_rows", "trees", "cat.item_rows", None),

        new("refine: search_titles, no filter", "refine", "lend.search_titles", None),
        new("refine: search_titles, by author", "refine", "lend.search_titles", new Dictionary<string, object?> { ["@author"] = "A. Writer" }),
        new("refine: search_titles, by genre and name", "refine", "lend.search_titles", new Dictionary<string, object?> { ["@genre"] = "fiction", ["@name"] = "T" }),
        new("refine: authors_report", "refine", "lend.authors_report", None),
        new("refine: authors_report, no titles", "refine", "lend.authors_report", None, NoLibrary),
        new("refine: get_member, with a home branch", "refine", "lend.get_member", new Dictionary<string, object?> { ["@member_id"] = 1 }),
        new("refine: get_member, no email or home branch", "refine", "lend.get_member", new Dictionary<string, object?> { ["@member_id"] = 2 }),
        new("refine: get_member, never borrowed", "refine", "lend.get_member", new Dictionary<string, object?> { ["@member_id"] = 4 }),
        new("refine: get_member, no such member", "refine", "lend.get_member", new Dictionary<string, object?> { ["@member_id"] = 999 }),
        new("refine: member_by_email", "refine", "lend.member_by_email", new Dictionary<string, object?> { ["@email"] = "cy@example.test" }),
        new("refine: member_by_email, with a nickname", "refine", "lend.member_by_email", new Dictionary<string, object?> { ["@email"] = "ann@example.test" }),
        new("refine: member_by_email, no such email", "refine", "lend.member_by_email", new Dictionary<string, object?> { ["@email"] = "none@example.test" }),
        new("refine: member_by_email, null", "refine", "lend.member_by_email", new Dictionary<string, object?> { ["@email"] = null }),
        new("refine: copy_at, shelved", "refine", "lend.copy_at", new Dictionary<string, object?> { ["@branch_id"] = 1, ["@copy_no"] = 1 }),
        new("refine: copy_at, no author", "refine", "lend.copy_at", new Dictionary<string, object?> { ["@branch_id"] = 2, ["@copy_no"] = 1 }),
        new("refine: copy_at, no shelf", "refine", "lend.copy_at", new Dictionary<string, object?> { ["@branch_id"] = 1, ["@copy_no"] = 2 }),
        new("refine: copy_at, no such copy", "refine", "lend.copy_at", new Dictionary<string, object?> { ["@branch_id"] = 9, ["@copy_no"] = 9 }),
        new("refine: open_loans", "refine", "lend.open_loans", None),
        new("refine: copies_at, two copies", "refine", "lend.copies_at", new Dictionary<string, object?> { ["@branch_id"] = 1 }),
        new("refine: copies_at, none", "refine", "lend.copies_at", new Dictionary<string, object?> { ["@branch_id"] = 4 }),
        new("refine: loan_totals", "refine", "lend.loan_totals", new Dictionary<string, object?> { ["@member_id"] = 1 }),
        new("refine: member_digests", "refine", "lend.member_digests", None),
        new("refine: branch_stats", "refine", "lend.branch_stats", None),
        new("refine: branch_stats, no loans", "refine", "lend.branch_stats", None, NoLoans),
        new("refine: overdue_members", "refine", "lend.overdue_members", new Dictionary<string, object?> { ["@today"] = new DateTime(2026, 11, 10) }),
        new("refine: overdue_members, none yet", "refine", "lend.overdue_members", new Dictionary<string, object?> { ["@today"] = new DateTime(2026, 1, 1) }),
        new("refine: catalogue_summary", "refine", "lend.catalogue_summary", None),
        new("refine: catalogue_summary, an empty library", "refine", "lend.catalogue_summary", None, NoLibrary),
        new("refine: latest_loan, returned and not", "refine", "lend.latest_loan", new Dictionary<string, object?> { ["@member_id"] = 1 }),
        new("refine: latest_loan, never borrowed", "refine", "lend.latest_loan", new Dictionary<string, object?> { ["@member_id"] = 4 }),
        new("refine: latest_loan, back with no fine", "refine", "lend.latest_loan", new Dictionary<string, object?> { ["@member_id"] = 3 }),
        new("flow: region_report", "flow", "flow.region_report", None),
        new("flow: region_report, web only", "flow", "flow.region_report", new Dictionary<string, object?> { ["@channel"] = "web" }),
        new("flow: region_report, a channel with no sales", "flow", "flow.region_report", new Dictionary<string, object?> { ["@channel"] = "post" }),
        new("flow: region_sales", "flow", "flow.region_sales", None),
        new("flow: region_sales, no sales", "flow", "flow.region_sales", None, NoSales),
        new("flow: sales_by_mode, sales", "flow", "flow.sales_by_mode", new Dictionary<string, object?> { ["@mode"] = (byte)1, ["@region"] = 1 }),
        new("flow: sales_by_mode, regions, no such region", "flow", "flow.sales_by_mode", new Dictionary<string, object?> { ["@mode"] = (byte)2, ["@region"] = 99 }),
        new("flow: sales_by_mode, regions with codes", "flow", "flow.sales_by_mode", new Dictionary<string, object?> { ["@mode"] = (byte)4, ["@region"] = 2 }),
        new("flow: sales_by_mode, returns early", "flow", "flow.sales_by_mode", new Dictionary<string, object?> { ["@mode"] = (byte)3 }),
        new("flow: part_list", "flow", "flow.part_list", None),
        new("flow: part_list, units cleared", "flow", "flow.part_list", new Dictionary<string, object?> { ["@with_units"] = false }),
        new("flow: org_chart", "flow", "flow.org_chart", None),
        new("flow: org_chart, no employees", "flow", "flow.org_chart", None, NoEmployees),
        new("flow: staff_moves", "flow", "flow.staff_moves", None),
        new("flow: staff_moves, assigned", "flow", "flow.staff_moves", new Dictionary<string, object?> { ["@assign"] = true }),
        new("flow: chain_up, from a fitter to the top", "flow", "flow.chain_up", new Dictionary<string, object?> { ["@employee"] = 4 }),
        new("flow: chain_up, no such employee", "flow", "flow.chain_up", new Dictionary<string, object?> { ["@employee"] = 99 }),
        new("flow: caught_report, no error", "flow", "flow.caught_report", None),
        new("flow: caught_report, error before the sales", "flow", "flow.caught_report", new Dictionary<string, object?> { ["@fail_at"] = (byte)1 }),
        new("flow: caught_report, error before the regions", "flow", "flow.caught_report", new Dictionary<string, object?> { ["@fail_at"] = (byte)2 }),
        new("flow: caught_report, error at the end", "flow", "flow.caught_report", new Dictionary<string, object?> { ["@fail_at"] = (byte)3 }),
        new("flow: notes_or_names, notes", "flow", "flow.notes_or_names", None),
        new("flow: notes_or_names, names", "flow", "flow.notes_or_names", new Dictionary<string, object?> { ["@notes"] = false }),
        new("flow: notes_or_names, no regions", "flow", "flow.notes_or_names", new Dictionary<string, object?> { ["@notes"] = false }, NoRegions),
        new("flow: bill_of_materials, two levels", "flow", "flow.bill_of_materials", new Dictionary<string, object?> { ["@assembly"] = 1 }),
        new("flow: bill_of_materials, a part with no components", "flow", "flow.bill_of_materials", new Dictionary<string, object?> { ["@assembly"] = 6 }),

        new("calls: oven_list", "calls", "calls.oven_list", None),
        new("calls: daily_report", "calls", "calls.daily_report", None),
        new("calls: daily_report, one day", "calls", "calls.daily_report", new Dictionary<string, object?> { ["@day"] = new DateTime(2026, 11, 3) }),
        new("calls: daily_report, with placeholders", "calls", "calls.daily_report", new Dictionary<string, object?> { ["@placeholder"] = true }),
        new("calls: batch_rows", "calls", "calls.batch_rows", None),
        new("calls: batch_rows, an oven without racks", "calls", "calls.batch_rows", new Dictionary<string, object?> { ["@oven"] = 2 }),
        new("calls: batch_rows, an oven with racks", "calls", "calls.batch_rows", new Dictionary<string, object?> { ["@oven"] = 1 }),
        new("calls: rack_sizes", "calls", "calls.rack_sizes", None),
        new("calls: batch_feed", "calls", "calls.batch_feed", None),
        new("calls: batch_feed, one oven", "calls", "calls.batch_feed", new Dictionary<string, object?> { ["@oven"] = 2 }),
        new("calls: batch_feed, nothing baked", "calls", "calls.batch_feed", None, NoBakery),
        new("calls: walk_even, no steps", "calls", "calls.walk_even", new Dictionary<string, object?> { ["@n"] = 0 }),
        new("calls: walk_even, one step", "calls", "calls.walk_even", new Dictionary<string, object?> { ["@n"] = 1 }),
        new("calls: walk_odd, no steps", "calls", "calls.walk_odd", new Dictionary<string, object?> { ["@n"] = 0 }),
        new("calls: walk_odd, three steps", "calls", "calls.walk_odd", new Dictionary<string, object?> { ["@n"] = 3 }),
        new("calls: ovens_or_batches, ovens", "calls", "calls.ovens_or_batches", new Dictionary<string, object?> { ["@ovens"] = true }),
        new("calls: ovens_or_batches, batches", "calls", "calls.ovens_or_batches", new Dictionary<string, object?> { ["@ovens"] = false }),
        new("calls: ovens_or_batches, an oven without racks", "calls", "calls.ovens_or_batches", new Dictionary<string, object?> { ["@ovens"] = false, ["@oven"] = 2 }),
        new("calls: ovens_or_batches, an oven with racks", "calls", "calls.ovens_or_batches", new Dictionary<string, object?> { ["@ovens"] = false, ["@oven"] = 1 }),
        new("calls: search_batches", "calls", "calls.search_batches", new Dictionary<string, object?> { ["@product"] = "%r%" }),
        new("calls: search_batches, no match", "calls", "calls.search_batches", new Dictionary<string, object?> { ["@product"] = "zzz" }),
        new("calls: rack_counts", "calls", "calls.rack_counts", None),
        new("calls: delivery_stage", "calls", "calls.delivery_stage", None),
        new("calls: recount_from, no steps", "calls", "calls.recount_from", new Dictionary<string, object?> { ["@n"] = 0 }),
        new("calls: recount_from, one step", "calls", "calls.recount_from", new Dictionary<string, object?> { ["@n"] = 1 }),
        new("calls: maybe_ovens, shown", "calls", "calls.maybe_ovens", new Dictionary<string, object?> { ["@show"] = true }),
        new("calls: maybe_ovens, not shown", "calls", "calls.maybe_ovens", new Dictionary<string, object?> { ["@show"] = false }),
        new("calls: maybe_then_batches, ovens first", "calls", "calls.maybe_then_batches", new Dictionary<string, object?> { ["@show"] = true }),
        new("calls: maybe_then_batches, no ovens", "calls", "calls.maybe_then_batches", new Dictionary<string, object?> { ["@show"] = false }),
        new("calls: search_with_ovens", "calls", "calls.search_with_ovens", new Dictionary<string, object?> { ["@product"] = "%e%" }),
        new("dml: list_parts, every part", "dml", "depot.list_parts", None),
        new("dml: list_parts, by code", "dml", "depot.list_parts", new Dictionary<string, object?> { ["@code"] = "B-200" }),
        new("dml: get_order, with lines and no note", "dml", "depot.get_order", new Dictionary<string, object?> { ["@order_id"] = 1 }),
        new("dml: get_order, no lines", "dml", "depot.get_order", new Dictionary<string, object?> { ["@order_id"] = 3 }),
        new("dml: get_order, no such order", "dml", "depot.get_order", new Dictionary<string, object?> { ["@order_id"] = 999 }),
        new("dml: log_error", "dml", "depot.log_error", new Dictionary<string, object?> { ["@procedure"] = "depot.test", ["@message"] = null }, Writes),
        new("dml: place_order", "dml", "depot.place_order", new Dictionary<string, object?> { ["@part_id"] = 2, ["@qty"] = 5 }, Writes),
        new("dml: place_order, a part with no stock, a note", "dml", "depot.place_order", new Dictionary<string, object?> { ["@part_id"] = 3, ["@qty"] = 1, ["@note"] = "call first" }, Writes),
        new("dml: cancel_order, no note", "dml", "depot.cancel_order", new Dictionary<string, object?> { ["@order_id"] = 1 }, Writes),
        new("dml: cancel_order, a note", "dml", "depot.cancel_order", new Dictionary<string, object?> { ["@order_id"] = 2 }, Writes),
        new("dml: cancel_order, no such order", "dml", "depot.cancel_order", new Dictionary<string, object?> { ["@order_id"] = 999 }, Writes),
        new("dml: sync_prices", "dml", "depot.sync_prices", None, Writes),
        new("dml: restock, one low part", "dml", "depot.restock", None, Writes),
        new("dml: restock, nothing low", "dml", "depot.restock", None, NothingLow),
        new("dml: restock, a shelved part last", "dml", "depot.restock", None, ShelvedLow),
        new("dml: purge", "dml", "depot.purge", None, Writes),
        new("dml: recent_note", "dml", "depot.recent_note", None),
        new("dml: add_note", "dml", "depot.add_note", new Dictionary<string, object?> { ["@note_id"] = 2, ["@text"] = "new" }, Writes),
        new("dml: peek_parts", "dml", "depot.peek_parts", None),
        new("dml: prune_prices", "dml", "depot.prune_prices", None, Writes),
        new("dml: low_floor, null when declared", "dml", "depot.low_floor", None),
        new("dml: low_floor, a value when declared", "dml", "depot.low_floor", new Dictionary<string, object?> { ["@start"] = 3 }),
        new("dml: add_note_by_synonym", "dml", "depot.add_note_by_synonym", new Dictionary<string, object?> { ["@note_id"] = 3, ["@text"] = "via" }, Writes),
        new("dml: pick_order_lines, no note", "dml", "depot.pick_order_lines", new Dictionary<string, object?> { ["@order_id"] = 1 }, Writes),
        new("dml: pick_order_lines, a note", "dml", "depot.pick_order_lines", new Dictionary<string, object?> { ["@order_id"] = 2 }, Writes),
        new("apply: list_purchases", "apply", "mart.list_purchases", None),
        new("apply: list_purchases, none", "apply", "mart.list_purchases", None, NoMarket),
        new("apply: purchases_json", "apply", "mart.purchases_json", None),
        new("apply: purchases_json, none", "apply", "mart.purchases_json", None, NoMarket),
        new("apply: customer_tree", "apply", "mart.customer_tree", None),
        new("apply: customer_tree, no purchases", "apply", "mart.customer_tree", None, NoPurchases),
        new("apply: customer_tree, no customers", "apply", "mart.customer_tree", None, NoMarket),
        new("apply: get_purchase, two lines", "apply", "mart.get_purchase", new Dictionary<string, object?> { ["@purchase_id"] = 10 }),
        new("apply: get_purchase, no lines", "apply", "mart.get_purchase", new Dictionary<string, object?> { ["@purchase_id"] = 11 }),
        new("apply: get_purchase, no such purchase", "apply", "mart.get_purchase", new Dictionary<string, object?> { ["@purchase_id"] = 99 }),
        new("apply: customer_purchases", "apply", "mart.customer_purchases", None),
        new("apply: last_purchase", "apply", "mart.last_purchase", None),
        new("apply: last_purchase, no purchases", "apply", "mart.last_purchase", None, NoPurchases),
        new("apply: customer_activity", "apply", "mart.customer_activity", None),
        new("apply: customer_activity, no purchases", "apply", "mart.customer_activity", None, NoPurchases),
        new("apply: product_sizes", "apply", "mart.product_sizes", None),
        new("apply: open_purchase_by_id, open", "apply", "mart.open_purchase_by_id", new Dictionary<string, object?> { ["@purchase_id"] = 10 }),
        new("apply: open_purchase_by_id, with a note", "apply", "mart.open_purchase_by_id", new Dictionary<string, object?> { ["@purchase_id"] = 12 }),
        new("apply: open_purchase_by_id, paid", "apply", "mart.open_purchase_by_id", new Dictionary<string, object?> { ["@purchase_id"] = 11 }),
        new("apply: open_contacts", "apply", "mart.open_contacts", None),
        new("apply: list_products", "apply", "mart.list_products", None),
        new("apply: purchase_overview", "apply", "mart.purchase_overview", None),
        new("apply: purchase_overview, none", "apply", "mart.purchase_overview", None, NoMarket),
        new("apply: purchase_lines, two lines", "apply", "mart.purchase_lines", new Dictionary<string, object?> { ["@purchase_id"] = 10 }),
        new("apply: purchase_lines, none", "apply", "mart.purchase_lines", new Dictionary<string, object?> { ["@purchase_id"] = 11 }),
        new("apply: price_versions, changed once", "apply", "mart.price_versions", new Dictionary<string, object?> { ["@product_id"] = 101 }),
        new("apply: price_versions, never changed", "apply", "mart.price_versions", new Dictionary<string, object?> { ["@product_id"] = 100 }),

        new("tabs: tab_summary, closed with items", "tabs", "cafe.tab_summary", new Dictionary<string, object?> { ["@tab_id"] = 10 }),
        new("tabs: tab_summary, open", "tabs", "cafe.tab_summary", new Dictionary<string, object?> { ["@tab_id"] = 11 }),
        new("tabs: tab_summary, no items, no nickname", "tabs", "cafe.tab_summary", new Dictionary<string, object?> { ["@tab_id"] = 13 }),
        new("tabs: tab_summary, no such tab", "tabs", "cafe.tab_summary", new Dictionary<string, object?> { ["@tab_id"] = 99 }),
        new("tabs: tab_receipt, a numeric note", "tabs", "cafe.tab_receipt", new Dictionary<string, object?> { ["@tab_id"] = 10 }),
        new("tabs: tab_receipt, a note that is not a number", "tabs", "cafe.tab_receipt", new Dictionary<string, object?> { ["@tab_id"] = 11 }),
        new("tabs: tab_receipt, no note, no nickname", "tabs", "cafe.tab_receipt", new Dictionary<string, object?> { ["@tab_id"] = 12 }),
        new("tabs: tab_receipt, no items", "tabs", "cafe.tab_receipt", new Dictionary<string, object?> { ["@tab_id"] = 13 }),
        new("tabs: tab_receipt, no such tab", "tabs", "cafe.tab_receipt", new Dictionary<string, object?> { ["@tab_id"] = 99 }),
        new("tabs: guest_tab_list, an open and a closed tab", "tabs", "cafe.guest_tab_list", new Dictionary<string, object?> { ["@guest_id"] = 1 }),
        new("tabs: guest_tab_list, no tabs", "tabs", "cafe.guest_tab_list", new Dictionary<string, object?> { ["@guest_id"] = 3 }),
        new("tabs: tab_lines_list, two lines", "tabs", "cafe.tab_lines_list", new Dictionary<string, object?> { ["@tab_id"] = 10 }),
        new("tabs: tab_lines_list, none", "tabs", "cafe.tab_lines_list", new Dictionary<string, object?> { ["@tab_id"] = 13 }),
        new("tabs: guests_with_open_tab", "tabs", "cafe.guests_with_open_tab", None),
        new("tabs: guests_with_open_tab, none open", "tabs", "cafe.guests_with_open_tab", None, NoOpenTabs),
        new("tabs: guest_labels", "tabs", "cafe.guest_labels", None),
        new("tabs: dirty_tab, with a note", "tabs", "cafe.dirty_tab", new Dictionary<string, object?> { ["@tab_id"] = 11 }),
        new("tabs: dirty_tab, no note", "tabs", "cafe.dirty_tab", new Dictionary<string, object?> { ["@tab_id"] = 12 }),
        new("tabs: open_tabs_payload", "tabs", "cafe.open_tabs_payload", None),
        new("tabs: open_tabs_payload, none open", "tabs", "cafe.open_tabs_payload", None, NoOpenTabs),
        new("tabs: guest_spending", "tabs", "cafe.guest_spending", None),
        new("tabs: guest_spending, no guests", "tabs", "cafe.guest_spending", None, NoCafe),
        new("tabs: menu_prices", "tabs", "cafe.menu_prices", None),
        new("cases: job_board", "cases", "desk.job_board", None),
        new("cases: job_card, two lines, one without a price", "cases", "desk.job_card", new Dictionary<string, object?> { ["@job_id"] = 11 }),
        new("cases: job_card, one free line", "cases", "desk.job_card", new Dictionary<string, object?> { ["@job_id"] = 12 }),
        new("cases: job_card, no lines", "cases", "desk.job_card", new Dictionary<string, object?> { ["@job_id"] = 10 }),
        new("cases: job_card, no such job", "cases", "desk.job_card", new Dictionary<string, object?> { ["@job_id"] = 99 }),
        new("cases: customer_jobs", "cases", "desk.customer_jobs", None),
        new("cases: status_totals", "cases", "desk.status_totals", None),
        new("cases: status_totals, done", "cases", "desk.status_totals", new Dictionary<string, object?> { ["@status"] = "D" }),
        new("cases: status_totals, no jobs", "cases", "desk.status_totals", None, NoDesk),
        new("sets: everyone", "sets", "rink.everyone", None),
        new("sets: session_board", "sets", "rink.session_board", None),
        new("sets: session_board, empty rink", "sets", "rink.session_board", None, NoRink),
        new("sets: regulars, a member who booked", "sets", "rink.regulars", new Dictionary<string, object?> { ["@member_id"] = 1 }),
        new("sets: regulars, a member who did not", "sets", "rink.regulars", new Dictionary<string, object?> { ["@member_id"] = 5 }),
        new("sets: counts", "sets", "rink.counts", None),
        new("sets: counts, empty rink", "sets", "rink.counts", None, NoRink),
        new("sets: lockers", "sets", "rink.lockers", None),
        new("sets: member_prefs", "sets", "rink.member_prefs", None),
        new("sets: member_prefs, empty rink", "sets", "rink.member_prefs", None, NoRink),
        new("sets: reach, a member and a guest", "sets", "rink.reach", new Dictionary<string, object?> { ["@person_id"] = 1, ["@session_id"] = 1 }),
        new("sets: reach, a member only, a lesson", "sets", "rink.reach", new Dictionary<string, object?> { ["@person_id"] = 3, ["@session_id"] = 2 }),
        new("sets: reach, a member and a guest without contacts", "sets", "rink.reach", new Dictionary<string, object?> { ["@person_id"] = 2, ["@session_id"] = 3 }),
        new("sets: reach, nobody", "sets", "rink.reach", new Dictionary<string, object?> { ["@person_id"] = 99, ["@session_id"] = 4 }),
        new("sets: busy_people", "sets", "rink.busy_people", None),
        new("sets: overlaps, counts kept", "sets", "rink.overlaps", new Dictionary<string, object?> { ["@min"] = 0L }),
        new("sets: overlaps, counts filtered out", "sets", "rink.overlaps", new Dictionary<string, object?> { ["@min"] = 100L }),
    ];

    /// <summary>Fixture procedures deliberately not run, each with the reason.</summary>
    public static IReadOnlyDictionary<string, string> NotRun { get; } = new Dictionary<string, string>
    {
        ["sales.order_report"] =
            "It reads sales.voucher and customer.nickname, which the fixture leaves out on purpose to show unknowns, so SQL Server cannot run it.",
        ["calls.picked_batches"] = "It reads its caller's #picked, so it runs only when called: through calls.daily_report, which is run.",
        ["calls.recount"] = "It reads and writes its caller's #counts, so it runs only when called: through calls.recount_from, which is run.",
        ["calls.add_placeholder"] = "It writes its caller's #picked, so it runs only when called: through calls.daily_report, which is run.",
    };

    public static ProcedureRun Named(string name) => All.Single(r => r.Name == name);
}
