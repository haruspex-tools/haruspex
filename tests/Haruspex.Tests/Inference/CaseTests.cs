using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// <c>CASE</c>, <c>IIF</c>, <c>CHOOSE</c> and <c>NULLIF</c>, and <c>COALESCE</c> and <c>ISNULL</c> over JSON. Each type,
/// reason to be null and way of nesting rests on an observation in the differential tests' <c>CaseObservationTests</c>, named
/// where it matters.
/// </summary>
public class CaseTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, x int NULL, tiny tinyint NULL, amount decimal(9,2) NULL, code char(4) NULL,
            short varchar(10) NULL, wide nvarchar(5) NULL, big bigint NOT NULL, label varchar(8) NOT NULL,
            status varchar(10) NULL CONSTRAINT ck_status CHECK (status IN ('open', 'shut')),
            pick tinyint NOT NULL CONSTRAINT ck_pick CHECK (pick BETWEEN 1 AND 3),
            wild tinyint NOT NULL CONSTRAINT ck_wild CHECK (wild BETWEEN 0 AND 3));
        GO
        CREATE TABLE dbo.u (id int NOT NULL PRIMARY KEY, t_id int NOT NULL, qty int NOT NULL, note varchar(20) NULL);
        GO
        """;

    private static IrDocument Procedure(string body, string parameters = "") =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p {parameters} AS\n{body}"));

    // Stricter than IrNavigation.Fields: the type must be an object type, not an array of one.
    private static EquatableArray<IrField> Fields(IrDocument doc, IrType type) => doc.Types[Assert.IsType<IrRef>(type).Name].Fields;

    /// <summary>The field <paramref name="name"/> of the first result's row, a rowset's or FOR JSON's.</summary>
    // Stricter than IrNavigation.Field: the first result must be an array of rows.
    private static IrField Field(IrDocument doc, string name)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        return Assert.Single(Fields(doc, ((IrArray)results[0].Type).Items), f => f.Name == name);
    }

    private static string[] Rules(IrDocument doc, string name) => [.. Field(doc, name).NullableBecause.Select(r => r.Rule)];

    /// <summary>The field's SQL type, or "unknown" when it is not typed.</summary>
    private static string Sql(IrDocument doc, string name) => Field(doc, name).Type switch
    {
        IrScalar scalar => scalar.Sql ?? "(none)",
        var other => other.GetType().Name,
    };

    /// <summary>The type and the reasons to be null, as one string: <c>varchar(6) no-else</c>.</summary>
    private static string Typed(IrDocument doc, string name) => string.Join(" ", [Sql(doc, name), .. Rules(doc, name)]);

    /// <summary>The field's exact value set, as <c>a|b</c>, or null when it has none.</summary>
    private static string? Values(IrDocument doc, string name) =>
        Field(doc, name).Type is IrScalar { Facets.Values: { Match: IrValueMatch.Exact } set }
            ? string.Join("|", set.Items.Select(i => i.Text))
            : null;

    private static bool IsUnknown(IrDocument doc, string name) => Field(doc, name).Type is IrUnknownType;

    // ---- types -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results()
    {
        // Case_iif_and_choose_take_the_type_of_highest_precedence_over_their_results.
        var doc = Procedure("""
            SELECT CASE WHEN id = 1 THEN 'a' ELSE 'longer' END AS s1, CASE WHEN id = 1 THEN 'a' ELSE N'bb' END AS s2,
                   CASE WHEN id = 1 THEN code ELSE 'abcdefgh' END AS s4, CASE WHEN id = 1 THEN code ELSE code END AS s5,
                   CASE WHEN id = 1 THEN code ELSE 'ab' END AS s6, CASE id WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END AS s7,
                   CASE WHEN id = 1 THEN 1 ELSE 2 END AS n1, CASE WHEN id = 1 THEN 1 ELSE 2.5 END AS n2,
                   CASE WHEN id = 1 THEN tiny ELSE 1 END AS n3, CASE WHEN id = 1 THEN x ELSE amount END AS n4,
                   CASE WHEN id = 1 THEN 1 ELSE 12345.5 END AS n5, CASE WHEN id = 1 THEN big ELSE x END AS n6,
                   CASE WHEN id = 1 THEN 1 ELSE 2147483648 END AS n7,
                   IIF(id = 1, 'a', 'bcd') AS i1, CHOOSE(id, 'a', 'bb', 'ccc') AS c1, CHOOSE(id, 1, 2.5) AS c2
            FROM dbo.t;
            """);
        Assert.Equal("varchar(6)", Sql(doc, "s1"));
        Assert.Equal("nvarchar(2)", Sql(doc, "s2"));
        Assert.Equal("varchar(8)", Sql(doc, "s4"));
        Assert.Equal("char(4)", Sql(doc, "s5"));
        Assert.Equal("varchar(4)", Sql(doc, "s6"));
        Assert.Equal("varchar(1)", Sql(doc, "s7"));
        Assert.Equal("int", Sql(doc, "n1"));
        Assert.Equal("numeric(2,1)", Sql(doc, "n2"));
        Assert.Equal("int", Sql(doc, "n3"));
        Assert.Equal("decimal(12,2)", Sql(doc, "n4"));
        Assert.Equal("numeric(6,1)", Sql(doc, "n5"));
        Assert.Equal("bigint", Sql(doc, "n6"));
        Assert.Equal("numeric(10,0)", Sql(doc, "n7"));
        Assert.Equal("varchar(3)", Sql(doc, "i1"));
        Assert.Equal("varchar(3)", Sql(doc, "c1"));
        Assert.Equal("numeric(2,1)", Sql(doc, "c2"));
    }

    [Fact]
    public void Results_of_types_sql_server_refuses_to_combine_are_not_typed()
    {
        // An int beside a string is int on the server, converting the string: a failure is an error, an overflow NULL under the
        // caller's options (SetTypeObservationTests). An int beside a uniqueidentifier is refused (206).
        var doc = Procedure("SELECT CASE WHEN id = 1 THEN x ELSE short END AS m, CASE WHEN id = 1 THEN NULL ELSE short END AS ok, CASE WHEN id = 1 THEN x ELSE NEWID() END AS no FROM dbo.t;");
        Assert.Equal("int nullable-column nullable-column arithmetic-overflow", Typed(doc, "m"));
        Assert.Equal("varchar(10)", Sql(doc, "ok"));
        Assert.True(IsUnknown(doc, "no"));
    }

    // ---- what is NULL ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_case_is_null_when_a_result_may_be_or_there_is_no_else()
    {
        // Case_without_else_choose_out_of_range_and_nullif_of_equal_values_are_null.
        var doc = Procedure("""
            SELECT CASE WHEN id = 1 THEN 'a' ELSE 'b' END AS full_case, CASE WHEN id = 1 THEN 'a' END AS no_else,
                   CASE WHEN id = 1 THEN NULL ELSE 'b' END AS null_result, CASE WHEN id = 1 THEN short ELSE label END AS nullable_result,
                   IIF(id = 1, 'a', 'b') AS iif_full, IIF(id = 1, NULL, 2) AS iif_null
            FROM dbo.t;
            """);
        Assert.Equal("varchar(1)", Typed(doc, "full_case"));
        Assert.Equal("varchar(1) no-else", Typed(doc, "no_else"));
        Assert.Equal("varchar(1) null-literal", Typed(doc, "null_result"));
        Assert.Equal("varchar(10) nullable-column", Typed(doc, "nullable_result"));
        Assert.Equal("varchar(1)", Typed(doc, "iif_full"));
        Assert.Equal("int null-literal", Typed(doc, "iif_null"));
    }

    [Fact]
    public void A_case_of_null_results_only_is_not_typed()
    {
        // A_case_whose_results_are_all_null_is_an_error_but_choose_of_nulls_is_null: CASE fails to be created; CHOOSE is an
        // int NULL, which is not worth modelling.
        var doc = Procedure("SELECT CHOOSE(id, NULL, NULL) AS c FROM dbo.t;");
        Assert.True(IsUnknown(doc, "c"));
    }

    [Fact]
    public void Choose_is_null_when_its_index_is_or_may_be_out_of_range()
    {
        // An index below 1 or above the count is NULL. An index whose CHECK keeps it in 1..n is always in range; an integer
        // literal in range too.
        var doc = Procedure("""
            SELECT CHOOSE(id, 'a', 'b') AS any_index, CHOOSE(tiny, 'a', 'b') AS null_index, CHOOSE(pick, 'a', 'b', 'c') AS checked,
                   CHOOSE(pick, 'a', 'b') AS too_few, CHOOSE(wild, 'a', 'b', 'c') AS from_zero, CHOOSE(2, 'a', label) AS literal,
                   CHOOSE(3, 'a', 'b') AS literal_out
            FROM dbo.t;
            """);
        Assert.Equal("varchar(1) out-of-range", Typed(doc, "any_index"));
        Assert.Equal("varchar(1) nullable-column out-of-range", Typed(doc, "null_index"));
        Assert.Equal("varchar(1)", Typed(doc, "checked"));
        Assert.Equal("varchar(1) out-of-range", Typed(doc, "too_few"));
        Assert.Equal("varchar(1) out-of-range", Typed(doc, "from_zero"));
        Assert.Equal("varchar(8)", Typed(doc, "literal"));
        Assert.Equal("varchar(1) out-of-range", Typed(doc, "literal_out"));
    }

    [Fact]
    public void Nullif_is_its_first_arguments_value_or_null()
    {
        // Nullif_is_its_first_arguments_type_but_an_integer_literal_is_the_smallest_integer_holding_it. Its value is the
        // first argument's, unchanged, so a CHECK set survives; with a NULL second argument nothing ever equals it.
        var doc = Procedure("""
            SELECT NULLIF(x, 1.5) AS a, NULLIF(short, N'x') AS b, NULLIF(label, '') AS c, NULLIF(status, 'open') AS d,
                   NULLIF(1, x) AS e, NULLIF(1000, x) AS f, NULLIF(100000, x) AS g, NULLIF(-1, x) AS h, NULLIF(label, NULL) AS i
            FROM dbo.t;
            """);
        Assert.Equal("int nullable-column nullif", Typed(doc, "a"));
        Assert.Equal("varchar(10) nullable-column nullif", Typed(doc, "b"));
        Assert.Equal("varchar(8) nullif", Typed(doc, "c"));
        Assert.Equal("varchar(10) nullable-column nullif", Typed(doc, "d"));
        Assert.NotNull(((IrScalar)Field(doc, "d").Type).Facets?.Values);
        Assert.Equal("tinyint nullif", Typed(doc, "e"));
        Assert.Equal("smallint nullif", Typed(doc, "f"));
        Assert.Equal("int nullif", Typed(doc, "g"));
        Assert.Equal("smallint nullif", Typed(doc, "h"));
        Assert.Equal("varchar(8)", Typed(doc, "i"));
    }

    // ---- branches refine --------------------------------------------------------------------------------------------

    [Fact]
    public void A_branch_knows_its_condition_held_and_every_earlier_one_failed()
    {
        // A_case_branch_runs_only_when_its_condition_is_true_and_later_ones_only_when_earlier_were_not.
        var doc = Procedure("""
            SELECT CASE WHEN x IS NULL THEN 0 ELSE x END AS a,
                   CASE WHEN x = 1 THEN 0 ELSE x END AS b,
                   CASE WHEN x IS NOT NULL THEN x ELSE 0 END AS c,
                   CASE WHEN x > 5 THEN x ELSE 0 END AS d,
                   CASE x WHEN 1 THEN x ELSE 0 END AS e,
                   CASE x WHEN 1 THEN 0 ELSE x END AS f,
                   CASE WHEN x IS NULL THEN 0 WHEN x > 5 THEN 1 ELSE x END AS g,
                   CASE WHEN NOT (x IS NULL) THEN x ELSE 0 END AS h,
                   CASE WHEN x IS NULL OR id = 1 THEN 0 ELSE x END AS i,
                   CASE WHEN x IS NULL AND id = 1 THEN 0 ELSE x END AS j,
                   IIF(x IS NULL, 0, x) AS k,
                   CASE WHEN short IS NULL THEN 'none' ELSE short END AS l,
                   CASE WHEN x IS NOT NULL THEN 0 ELSE x END AS m
            FROM dbo.t;
            """);
        Assert.Equal("int", Typed(doc, "a"));
        Assert.Equal("int nullable-column", Typed(doc, "b"));
        Assert.Equal("int", Typed(doc, "c"));
        Assert.Equal("int", Typed(doc, "d"));
        Assert.Equal("int", Typed(doc, "e"));
        Assert.Equal("int nullable-column", Typed(doc, "f"));
        Assert.Equal("int", Typed(doc, "g"));
        Assert.Equal("int", Typed(doc, "h"));
        Assert.Equal("int", Typed(doc, "i"));
        Assert.Equal("int nullable-column", Typed(doc, "j"));
        Assert.Equal("int", Typed(doc, "k"));
        Assert.Equal("varchar(10)", Typed(doc, "l"));
        Assert.Equal("int nullable-column", Typed(doc, "m"));
    }

    [Fact]
    public void A_branch_proves_an_outer_joins_row_variables_and_aggregates_too()
    {
        // A tested column of an outer join's optional side is not null, so its row matched; a variable or an aggregate is
        // the same value in WHEN and THEN.
        var doc = Procedure("""
            DECLARE @v int = NULL;
            SELECT CASE WHEN u.note IS NULL THEN 'none' ELSE u.note END AS note,
                   CASE WHEN u.id IS NOT NULL THEN u.qty ELSE 0 END AS qty,
                   CASE WHEN u.id IS NOT NULL THEN u.note ELSE '' END AS other,
                   CASE WHEN @v IS NULL THEN 0 ELSE @v END AS v,
                   CASE WHEN @p IS NULL THEN 0 ELSE @p END AS p
            FROM dbo.t AS t LEFT JOIN dbo.u AS u ON u.t_id = t.id;
            SELECT CASE WHEN SUM(x) IS NULL THEN 0 ELSE SUM(x) END AS total, CASE WHEN MAX(x) > 0 THEN MAX(x) ELSE 0 END AS most FROM dbo.t;
            """,
            "@p int");
        Assert.Equal("varchar(20)", Typed(doc, "note"));
        Assert.Equal("int", Typed(doc, "qty"));
        Assert.Equal("varchar(20) nullable-column", Typed(doc, "other"));
        Assert.Equal("int", Typed(doc, "v"));
        Assert.Equal("int", Typed(doc, "p"));

        var aggregates = doc.Types[((IrRef)((IrArray)doc.Objects[0].Results![1].Type).Items).Name].Fields;
        Assert.Empty(Assert.Single(aggregates, f => f.Name == "total").NullableBecause);
        Assert.Empty(Assert.Single(aggregates, f => f.Name == "most").NullableBecause);
    }

    [Fact]
    public void A_branch_proves_a_rolled_up_column_non_null()
    {
        // The super-aggregate row's NULL is tested like any other, after grouping, so the ELSE never sees it.
        var doc = Procedure("SELECT CASE WHEN short IS NULL THEN 'total' ELSE short END AS g, COUNT_BIG(*) AS n FROM dbo.t GROUP BY ROLLUP (short);");
        Assert.Equal("varchar(10)", Typed(doc, "g"));
    }

    [Fact]
    public void A_condition_on_another_value_proves_nothing_about_a_subquery_read_again()
    {
        // A subquery may give another value the second time it is read (Coalesce_reads_a_subquery_twice…), so testing it in
        // WHEN proves nothing about it in THEN.
        var doc = Procedure("SELECT CASE WHEN (SELECT MAX(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) IS NULL THEN 0 ELSE (SELECT MAX(u.qty) FROM dbo.u AS u WHERE u.t_id = t.id) END AS q FROM dbo.t AS t;");
        Assert.NotEmpty(Rules(doc, "q"));
    }

    // ---- closed value sets ------------------------------------------------------------------------------------------

    [Fact]
    public void Literal_results_are_an_exact_set_of_those_values()
    {
        // A_literal_result_keeps_its_trailing_spaces_and_a_fixed_length_result_pads: a CASE of literals is varchar, so each is
        // given exactly, its trailing spaces too. Literals in conditions do not count, a NULL result is not a member, and a
        // value given twice is one member.
        var doc = Procedure("""
            SELECT CASE status WHEN 'open' THEN 'Open' WHEN 'shut' THEN 'Closed' ELSE 'Unknown' END AS label,
                   CASE WHEN id = 1 THEN 'a ' WHEN id = 2 THEN 'b' WHEN id = 3 THEN 'a ' END AS spaced,
                   CASE WHEN id = 1 THEN NULL ELSE N'x' END AS with_null,
                   CASE WHEN id = 1 THEN 1 WHEN id = 2 THEN -5 ELSE 2.5 END AS number,
                   CASE WHEN id = 1 THEN 'a' ELSE short END AS mixed,
                   IIF(id = 1, 'yes', 'no') AS yes_no, CHOOSE(pick, 'low', 'mid', 'high') AS level
            FROM dbo.t;
            """);
        Assert.Equal("Closed|Open|Unknown", Values(doc, "label"));
        Assert.Equal("a |b", Values(doc, "spaced"));
        Assert.Equal("varchar(2) no-else", Typed(doc, "spaced"));
        Assert.Equal("x", Values(doc, "with_null"));
        Assert.Equal("nvarchar(1) null-literal", Typed(doc, "with_null"));
        Assert.Equal("-5|1|2.5", Values(doc, "number"));
        Assert.Null(Values(doc, "mixed"));
        Assert.Equal("no|yes", Values(doc, "yes_no"));
        Assert.Equal("high|low|mid", Values(doc, "level"));

        var from = ((IrScalar)Field(doc, "label").Type).Facets!.Values!.From;
        Assert.Equal(IrFactSources.Expression, Assert.Single(from).Kind);
    }

    // ---- COALESCE and ISNULL over JSON -------------------------------------------------------------------------------

    private const string Lines = "(SELECT u.qty FROM dbo.u AS u WHERE u.t_id = t.id FOR JSON PATH)";

    [Fact]
    public void Isnull_over_a_for_json_subquery_with_an_empty_array_is_json_never_null()
    {
        // Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json: the fallback '[]' is written as it is.
        // The empty array means the array may have no items, so the subquery's minItems goes.
        var doc = Procedure($"SELECT t.id, ISNULL({Lines}, '[]') AS lines, ISNULL({Lines}, N'[]') AS wide FROM dbo.t AS t FOR JSON PATH;");
        var lines = Field(doc, "lines");
        Assert.Empty(lines.NullableBecause);
        Assert.Equal(IrPresence.Always, lines.Presence);
        var array = Assert.IsType<IrArray>(lines.Type);
        Assert.Null(array.MinItems);
        Assert.Equal("qty", Assert.Single(Fields(doc, array.Items)).Name);
        Assert.IsType<IrArray>(Field(doc, "wide").Type);
    }

    [Fact]
    public void Coalesce_over_a_for_json_subquery_is_text_and_may_be_null()
    {
        // COALESCE nests as text (Coalesce_over_a_for_json_subquery_nests_as_escaped_text_and_isnull_as_json), and reads the
        // subquery twice (Coalesce_reads_a_for_json_subquery_twice_so_it_can_be_null_though_its_fallback_is_not).
        var doc = Procedure($"SELECT t.id, COALESCE({Lines}, '[]') AS lines FROM dbo.t AS t FOR JSON PATH;");
        var lines = Field(doc, "lines");
        Assert.Equal(["read-twice"], Rules(doc, "lines"));
        var text = Assert.IsType<IrJsonText>(lines.Type);
        var array = Assert.IsType<IrArray>(text.Content);
        Assert.Null(array.MinItems);
    }

    [Fact]
    public void Json_query_over_coalesce_makes_it_json_again()
    {
        // Json_query_over_coalesce_makes_it_json_again.
        var doc = Procedure($"SELECT t.id, JSON_QUERY(COALESCE({Lines}, '[]')) AS lines, JSON_QUERY(ISNULL({Lines}, '[]')) AS once FROM dbo.t AS t FOR JSON PATH;");
        var array = Assert.IsType<IrArray>(Field(doc, "lines").Type);
        Assert.Null(array.MinItems);
        Assert.Equal(["read-twice"], Rules(doc, "lines"));
        Assert.IsType<IrArray>(Field(doc, "once").Type);
        Assert.Empty(Rules(doc, "once"));
    }

    [Fact]
    public void In_a_rowset_isnull_and_coalesce_over_json_are_json_text()
    {
        // In_a_rowset_coalesce_and_isnull_over_json_are_nvarchar_max_text.
        var doc = Procedure($"SELECT t.id, ISNULL({Lines}, '[]') AS once, COALESCE({Lines}, '[]') AS twice FROM dbo.t AS t;");
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "once").Type).Content);
        Assert.Empty(Rules(doc, "once"));
        Assert.IsType<IrArray>(Assert.IsType<IrJsonText>(Field(doc, "twice").Type).Content);
        Assert.Equal(["read-twice"], Rules(doc, "twice"));
    }

    [Fact]
    public void An_empty_object_fallback_is_a_variant_of_its_own()
    {
        // Isnull_writes_its_fallback_into_the_json_as_it_is; Without_its_array_wrapper_the_subquery_is_text_in_coalesce_and_isnull_until_json_query.
        var doc = Procedure($$"""
            SELECT t.id, ISNULL({{Lines}}, '{}') AS lines,
                   JSON_QUERY(ISNULL((SELECT TOP (1) u.qty FROM dbo.u AS u WHERE u.t_id = t.id ORDER BY u.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}')) AS first_line,
                   ISNULL((SELECT TOP (1) u.qty FROM dbo.u AS u WHERE u.t_id = t.id ORDER BY u.id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '{}') AS first_text
            FROM dbo.t AS t FOR JSON PATH;
            """);
        var lines = Assert.IsType<IrUnion>(Field(doc, "lines").Type);
        Assert.Equal(2, lines.Variants.Count);
        Assert.Contains(lines.Variants, v => v is IrArray);
        Assert.Contains(lines.Variants, v => v is IrRef r && doc.Types[r.Name].Fields.Count == 0);
        Assert.Empty(Rules(doc, "lines"));

        var first = Assert.IsType<IrUnion>(Field(doc, "first_line").Type);
        Assert.Equal(2, first.Variants.Count);
        Assert.Empty(Rules(doc, "first_line"));
        Assert.IsType<IrUnion>(Assert.IsType<IrJsonText>(Field(doc, "first_text").Type).Content);
    }

    [Fact]
    public void A_fallback_that_is_not_an_empty_array_or_object_is_not_typed_where_it_is_written_as_json()
    {
        // ISNULL writes it unchecked: 'null' is JSON null that is not SQL NULL, so the key stays; '' and 'x' are not JSON.
        // Through COALESCE it is text: JSON text of unknown content for 'null', and a plain string for 'x'.
        var doc = Procedure($"""
            SELECT t.id, ISNULL({Lines}, 'null') AS a, ISNULL({Lines}, '') AS b, ISNULL({Lines}, t.short) AS c,
                   COALESCE({Lines}, 'null') AS d, COALESCE({Lines}, 'x') AS e, ISNULL({Lines}, {Lines}) AS f
            FROM dbo.t AS t FOR JSON PATH;
            """);
        Assert.True(IsUnknown(doc, "a"));
        Assert.True(IsUnknown(doc, "b"));
        Assert.True(IsUnknown(doc, "c"));
        Assert.Null(Assert.IsType<IrJsonText>(Field(doc, "d").Type).Content);
        Assert.Equal("nvarchar(max)", Sql(doc, "e"));
        Assert.IsType<IrArray>(Field(doc, "f").Type);
        Assert.Equal(["empty-subquery"], Rules(doc, "f"));
    }

    [Fact]
    public void Case_iif_choose_nullif_and_coalesce_over_json_that_the_server_may_fold_are_not_typed()
    {
        // Case_iif_choose_and_coalesce_over_json_are_text_unless_the_server_folds_them_to_one_branch: whether they nest as
        // JSON depends on the optimiser. COALESCE over a FOR JSON subquery is never folded, so only that is typed.
        var doc = Procedure($"""
            SELECT t.id, CASE WHEN t.id = 1 THEN {Lines} ELSE '[]' END AS a, IIF(t.id = 1, {Lines}, '[]') AS b,
                   CHOOSE(t.id, {Lines}, '[]') AS c, NULLIF({Lines}, '[]') AS d, COALESCE(JSON_OBJECT('a': t.id), '[]') AS e
            FROM dbo.t AS t FOR JSON PATH;
            """);
        foreach (var name in new[] { "a", "b", "c", "d", "e" })
            Assert.True(IsUnknown(doc, name), name);
    }
}
