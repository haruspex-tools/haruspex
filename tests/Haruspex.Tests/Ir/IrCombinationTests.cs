using Haruspex.Ir;

namespace Haruspex.Tests.Ir;

/// <summary>
/// Two documents combined (<see cref="IrCombination"/>): the types a new application is generated with when it may meet the
/// database before its change (the baseline) or after it (the candidate). Results widen, so every value either database returns
/// is accepted; parameters narrow, so every call the function makes is one both procedures take; and what can't be bridged in
/// one call is reported and left out. Each case infers the same procedure from two versions of its SQL, as a change to a database
/// would; <c>CombinedTypesTests</c>, in the differential tests, calls pairs like them through the combined functions on a server.
/// </summary>
public class IrCombinationTests
{
    private const string Orders = """
        CREATE TABLE shop.orders (
            order_id int NOT NULL PRIMARY KEY,
            customer nvarchar(50) NOT NULL,
            note nvarchar(200) NULL,
            status varchar(10) NOT NULL CHECK (status IN ('open', 'closed')),
            channel varchar(10) NULL,
            qty tinyint NOT NULL,
            total decimal(10, 2) NOT NULL);
        """;

    private const string GetOrder = "CREATE PROCEDURE shop.get_order @order_id int AS SELECT o.order_id, o.status FROM shop.orders AS o WHERE o.order_id = @order_id;";

    private static IrDocument Infer(string procedures, string tables = Orders) =>
        InferProcedure.Infer("shop.*", ("tables.sql", tables), ("procedures.sql", procedures));

    private static IrCombined Combine(IrDocument baseline, IrDocument candidate, bool strict = false) =>
        IrCombination.Of(baseline, candidate, new IrFitOptions { StrictObjects = strict });

    private static string Edit(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }

    /// <summary>The field of the combined document's first result's row type, by name.</summary>
    private static IrField Field(IrDocument document, string procedure, string name, int set = 0)
    {
        var result = document.Objects.Single(o => o.Id == procedure).Results![set];
        var row = (IrRef)((IrArray)result.Type).Items;
        return document.Types[row.Name].Fields.Single(f => f.Name == name);
    }

    private static IrParameter Parameter(IrDocument document, string procedure, string name) =>
        document.Objects.Single(o => o.Id == procedure).Parameters.Single(p => p.Name == name);

    /// <summary>
    /// The combined document fits both databases, as the fits-within operation judges it: an application built against it meeting
    /// either refuses nothing. Every case asserts it.
    /// </summary>
    private static void FitsBoth(IrCombined combined, IrDocument baseline, IrDocument candidate, bool strict = false)
    {
        var options = new IrFitOptions { StrictObjects = strict };
        foreach (var database in new[] { baseline, candidate })
        {
            var refused = IrFit.Of(combined.Document, database, options).Where(f => f.Verdict == IrFitVerdict.Refused).ToList();
            Assert.True(refused.Count == 0, string.Join("\n", refused));
        }
    }

    [Fact]
    public void A_document_combined_with_itself_says_what_it_says_and_names_its_baseline()
    {
        var document = Infer(GetOrder);

        var combined = Combine(document, document);

        Assert.Empty(combined.Findings);
        Assert.Equal(document with { Source = document.Source with { Baseline = document.Source } }, combined.Document);
        Assert.Equal("0.24", combined.Document.Version);
    }

    [Fact]
    public void A_key_only_the_candidate_returns_may_be_left_out_and_a_value_set_that_grew_is_the_union()
    {
        // The brief's example: { order_id, status } before, { order_id, status, channel } after, and 'held' a new status.
        var baseline = Infer(GetOrder);
        var candidate = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.channel FROM"), Edit(Orders, "('open', 'closed')", "('open', 'closed', 'held')"));

        var combined = Combine(baseline, candidate);

        Assert.Empty(combined.Findings);
        var channel = Field(combined.Document, "shop.get_order", "channel");
        Assert.Equal(IrPresence.Optional, channel.Presence);
        Assert.True(channel.Nullable);
        Assert.Equal(["closed", "held", "open"], ((IrScalar)Field(combined.Document, "shop.get_order", "status").Type).Facets!.Values!.Items.Select(v => v.Text).Order(StringComparer.Ordinal));
        FitsBoth(combined, baseline, candidate);
        FitsBoth(combined, baseline, candidate, strict: true);
    }

    [Fact]
    public void A_key_only_the_baseline_returns_may_be_left_out()
    {
        var baseline = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.qty FROM"));
        var candidate = Infer(GetOrder);

        var combined = Combine(baseline, candidate);

        var qty = Field(combined.Document, "shop.get_order", "qty");
        Assert.Equal(IrPresence.Optional, qty.Presence);
        Assert.False(qty.Nullable);
        FitsBoth(combined, baseline, candidate);
    }

    [Fact]
    public void A_value_nullable_in_either_is_nullable_and_a_length_is_the_longer()
    {
        var baseline = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.customer FROM"));
        var candidate = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.customer FROM"), Edit(Orders, "customer nvarchar(50) NOT NULL", "customer nvarchar(80) NULL"));

        var combined = Combine(baseline, candidate);

        var customer = Field(combined.Document, "shop.get_order", "customer");
        Assert.True(customer.Nullable);
        Assert.Equal(IrPresence.Always, customer.Presence);
        Assert.Equal(80, ((IrScalar)customer.Type).Facets!.Length!.Value);
        FitsBoth(combined, baseline, candidate);
        FitsBoth(combined, candidate, baseline);
    }

    [Fact]
    public void A_value_of_another_kind_is_a_union_of_both()
    {
        var baseline = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.qty AS amount FROM"));
        var candidate = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.customer AS amount FROM"));

        var combined = Combine(baseline, candidate);

        var amount = Assert.IsType<IrUnion>(Field(combined.Document, "shop.get_order", "amount").Type);
        Assert.Equal([IrScalarKind.String, IrScalarKind.Number], amount.Variants.Select(v => ((IrScalar)v).Kind));
        FitsBoth(combined, baseline, candidate);
    }

    [Fact]
    public void Integers_of_another_size_are_the_wider()
    {
        var baseline = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.qty FROM"));
        var candidate = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.qty FROM"), Edit(Orders, "qty tinyint", "qty smallint"));

        var combined = Combine(baseline, candidate);

        Assert.Equal(IrFormat.Int16, ((IrScalar)Field(combined.Document, "shop.get_order", "qty").Type).Format);
        FitsBoth(combined, baseline, candidate);
        FitsBoth(Combine(candidate, baseline), candidate, baseline);
    }

    [Fact]
    public void A_trailing_result_set_in_only_one_may_be_missing_against_the_other()
    {
        var one = Infer(GetOrder);
        var two = Infer(Edit(GetOrder, "= @order_id;", "= @order_id; SELECT o.qty FROM shop.orders AS o;"));

        foreach (var (baseline, candidate) in new[] { (one, two), (two, one) })
        {
            var combined = Combine(baseline, candidate);

            Assert.Empty(combined.Findings);
            var results = combined.Document.Objects.Single().Results!;
            Assert.Equal(2, results.Count);
            Assert.False(results[0].MayBeAbsent);
            Assert.Equal([IrNullRule.NotInBoth], results[1].AbsentBecause.Select(r => r.Rule));
            FitsBoth(combined, baseline, candidate);
            FitsBoth(combined, baseline, candidate, strict: true);
        }
    }

    [Fact]
    public void A_parameter_only_the_candidate_has_is_left_out_while_it_has_a_default()
    {
        var baseline = Infer(GetOrder);
        var candidate = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @channel varchar(10) = 'web' AS"));

        var combined = Combine(baseline, candidate);

        Assert.Empty(combined.Findings);
        Assert.Equal(["@order_id"], combined.Document.Objects.Single().Parameters.Select(p => p.Name));
        FitsBoth(combined, baseline, candidate);
    }

    [Fact]
    public void A_parameter_only_the_baseline_has_is_left_out_while_it_has_a_default()
    {
        var baseline = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @channel varchar(10) = 'web' AS"));
        var candidate = Infer(GetOrder);

        var combined = Combine(baseline, candidate);

        Assert.Empty(combined.Findings);
        Assert.Equal(["@order_id"], combined.Document.Objects.Single().Parameters.Select(p => p.Name));
        FitsBoth(combined, baseline, candidate);
    }

    [Fact]
    public void A_new_required_parameter_can_t_be_bridged_and_its_procedure_is_left_out()
    {
        var baseline = Infer(GetOrder);
        var candidate = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @channel varchar(10) AS"));

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["unbridgeable: shop.get_order parameters[\"@channel\"]: gained the required parameter @channel, which the baseline's procedure doesn't take: give it a default first, or ship the change in two releases"],
            combined.Findings.Select(f => f.ToString()));
        Assert.Empty(combined.Document.Objects);
        Assert.Empty(combined.Document.Types);
    }

    [Fact]
    public void A_removed_parameter_the_baseline_requires_can_t_be_bridged()
    {
        var baseline = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @channel varchar(10) AS"));
        var candidate = Infer(GetOrder);

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["unbridgeable: shop.get_order parameters[\"@channel\"]: lost the parameter @channel, which the baseline's procedure requires: give it a default first, or ship the change in two releases"],
            combined.Findings.Select(f => f.ToString()));
        Assert.Empty(combined.Document.Objects);
    }

    [Fact]
    public void A_parameter_takes_the_narrower_of_two_types_and_may_be_left_out_only_when_both_have_a_default()
    {
        var baseline = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @qty int = 1, @code nvarchar(20) = N'' AS"));
        var candidate = Infer(Edit(GetOrder, "@order_id int AS", "@order_id int, @qty tinyint = 1, @code nvarchar(10) AS"));

        var combined = Combine(baseline, candidate);

        Assert.Empty(combined.Findings);
        var qty = Parameter(combined.Document, "shop.get_order", "@qty");
        Assert.Equal((IrFormat.Uint8, true), (((IrScalar)qty.Type).Format, qty.HasDefault));
        var code = Parameter(combined.Document, "shop.get_order", "@code");
        Assert.Equal((10, false), (((IrScalar)code.Type).Facets!.Length!.Value, code.HasDefault));
        FitsBoth(combined, baseline, candidate);
        FitsBoth(Combine(candidate, baseline), candidate, baseline);
    }

    [Fact]
    public void A_parameter_of_another_kind_can_t_be_bridged()
    {
        var baseline = Infer(GetOrder);
        var candidate = Infer(Edit(GetOrder, "@order_id int AS", "@order_id uniqueidentifier AS").Replace("WHERE o.order_id = @order_id", "WHERE @order_id IS NOT NULL", StringComparison.Ordinal));

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["unbridgeable: shop.get_order parameters[\"@order_id\"]: changed type from number (int32) to uuid, which have no value in common the function could send: ship the change in two releases"],
            combined.Findings.Select(f => f.ToString()));
    }

    [Fact]
    public void A_procedure_in_only_one_database_is_left_out_and_said()
    {
        const string Other = "CREATE PROCEDURE shop.count_orders AS SELECT COUNT(*) AS n FROM shop.orders AS o;";
        var baseline = Infer(GetOrder + "\nGO\n" + Other.Replace("count_orders", "list_old", StringComparison.Ordinal));
        var candidate = Infer(GetOrder + "\nGO\n" + Other);

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            [
                "only in the candidate: shop.count_orders: the baseline's database doesn't have it, so the application can't call it until the change is deployed",
                "only in the baseline: shop.list_old: the candidate's database doesn't have it, so the application can't call it once the change is deployed",
            ],
            combined.Findings.Select(f => f.ToString()));
        Assert.Equal(["shop.get_order"], combined.Document.Objects.Select(o => o.Id));
    }

    [Fact]
    public void A_key_gone_and_one_appeared_in_the_same_row_is_said_as_a_possible_rename_and_both_may_be_left_out()
    {
        var baseline = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.customer AS customerName FROM"));
        var candidate = Infer(Edit(GetOrder, "o.status FROM", "o.status, o.customer AS customer_name FROM"));

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["possible rename: shop.get_order results[0][]: customerName went and customer_name appeared; if it was renamed, read both until the baseline moves on"],
            combined.Findings.Select(f => f.ToString()));
        Assert.Equal(IrPresence.Optional, Field(combined.Document, "shop.get_order", "customerName").Presence);
        Assert.Equal(IrPresence.Optional, Field(combined.Document, "shop.get_order", "customer_name").Presence);
        FitsBoth(combined, baseline, candidate);
    }

    [Fact]
    public void A_result_that_changes_from_rows_to_JSON_can_t_be_bridged()
    {
        var baseline = Infer(GetOrder);
        var candidate = Infer(Edit(GetOrder, "= @order_id;", "= @order_id FOR JSON PATH;"));

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["unbridgeable: shop.get_order results[0]: changed from rows to JSON, which one function can't read both ways: ship the change in two releases"],
            combined.Findings.Select(f => f.ToString()));
    }

    [Fact]
    public void A_JSON_key_left_out_when_null_in_one_and_sent_as_null_in_the_other_may_be_left_out_or_null()
    {
        const string Json = "CREATE PROCEDURE shop.get_card @order_id int AS SELECT o.order_id, o.note FROM shop.orders AS o WHERE o.order_id = @order_id FOR JSON PATH;";
        var baseline = Infer(Json);
        var candidate = Infer(Edit(Json, "FOR JSON PATH;", "FOR JSON PATH, INCLUDE_NULL_VALUES;"));

        foreach (var (a, b) in new[] { (baseline, candidate), (candidate, baseline) })
        {
            var combined = Combine(a, b);

            var note = Field(combined.Document, "shop.get_card", "note");
            Assert.Equal((IrPresence.Optional, true), (note.Presence, note.Nullable));
            FitsBoth(combined, a, b);
        }
    }

    [Fact]
    public void A_query_file_whose_text_changed_since_the_baseline_can_t_be_combined()
    {
        var document = Infer(GetOrder);
        var query = new IrObject("queries/find.sql", "Find", IrObjectKind.Query, "queries/find.sql:1:1", [], []) { Text = "SELECT 1 AS a;" };
        var baseline = document with { Objects = [.. document.Objects, query] };
        var candidate = document with { Objects = [.. document.Objects, query with { Text = "SELECT 2 AS a;" }] };

        var combined = Combine(baseline, candidate);

        Assert.Equal(
            ["unbridgeable: queries/find.sql: its text changed since the baseline, which says what the old text returns, not the new: give the baseline as a source, so that this text is read against it"],
            combined.Findings.Select(f => f.ToString()));
    }
}
