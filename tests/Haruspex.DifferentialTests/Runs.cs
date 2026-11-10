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
    public static IReadOnlyList<string> Fixtures { get; } = ["orders", "values", "checks", "payloads", "trees", "flow"];

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

    private static readonly Dictionary<string, object?> None = [];

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
        new("flow: bill_of_materials, two levels", "flow", "flow.bill_of_materials", new Dictionary<string, object?> { ["@assembly"] = 1 }),
        new("flow: bill_of_materials, a part with no components", "flow", "flow.bill_of_materials", new Dictionary<string, object?> { ["@assembly"] = 6 }),
    ];

    /// <summary>Fixture procedures deliberately not run, each with the reason.</summary>
    public static IReadOnlyDictionary<string, string> NotRun { get; } = new Dictionary<string, string>
    {
        ["sales.order_report"] =
            "It reads sales.voucher and customer.nickname, which the fixture leaves out on purpose to show unknowns, so SQL Server cannot run it.",
    };

    public static ProcedureRun Named(string name) => All.Single(r => r.Name == name);
}
