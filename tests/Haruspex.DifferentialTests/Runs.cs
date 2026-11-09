namespace Haruspex.DifferentialTests;

/// <summary>One call of a procedure, against a fixture's seeded database.</summary>
/// <param name="Name">A unique name for the run, shown in the test's name.</param>
/// <param name="Fixture">The folder under <c>tests/fixtures</c>.</param>
/// <param name="Procedure">The procedure's id.</param>
/// <param name="Parameters">The arguments, by parameter name.</param>
/// <param name="Setup">
/// SQL run first, in a transaction rolled back afterwards: a way to see the tables in a state the seed does not
/// leave them in, such as empty, so that a procedure without parameters can still be run with no rows.
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
    public static IReadOnlyList<string> Fixtures { get; } = ["orders", "values", "checks"];

    /// <summary>
    /// The fixtures whose seed must reach every edge the IR claims (Coverage.MissingEdges): each bound, each value
    /// of a set, each full length. They are the fixtures about scalar facts. <c>orders</c> is about query shapes,
    /// and its scalars are the same types <c>values</c> covers at their edges; seeding its ids and totals at
    /// int.MinValue would only obscure what it shows.
    /// </summary>
    public static IReadOnlyList<string> EdgeFixtures { get; } = ["values", "checks"];

    private const string NoOrders = "DELETE FROM sales.order_line; DELETE FROM sales.[order]; DELETE FROM sales.customer;";
    private const string NoSamples = "DELETE FROM vals.sample_tag; DELETE FROM vals.sample;";
    private const string NoTickets = "DELETE FROM chk.ticket_event; DELETE FROM chk.ticket;";

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
    ];

    /// <summary>Fixture procedures deliberately not run, each with the reason.</summary>
    public static IReadOnlyDictionary<string, string> NotRun { get; } = new Dictionary<string, string>
    {
        ["sales.order_report"] =
            "It reads sales.voucher and customer.nickname, which the fixture leaves out on purpose to show unknowns, so SQL Server cannot run it.",
    };

    public static ProcedureRun Named(string name) => All.Single(r => r.Name == name);
}
