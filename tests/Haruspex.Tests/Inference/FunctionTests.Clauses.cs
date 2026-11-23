using Haruspex.Ir;

namespace Haruspex.Tests.Inference;

/// <summary>
/// A clause of a call (<c>DISTINCT</c>, <c>WITHIN GROUP</c>, <c>IGNORE NULLS</c>, <c>TRIM</c>'s side, <c>RETURNING</c>, <c>WITH
/// ARRAY WRAPPER</c>, …) is typed only by a row that reads it, and makes any other call unknown; <c>COLLATE</c> keeps a Unicode
/// string and only the type of any other (decision 0113; observed in the differential tests' <c>ClauseObservationTests</c>).
/// </summary>
public partial class FunctionTests
{
    /// <summary>A field's facets.</summary>
    private static IrFacets? Facets(IrDocument doc, string name) => Assert.IsType<IrScalar>(Field(doc, name).Type).Facets;

    /// <summary>A field's set of values, when it has one.</summary>
    private static IrValueSet? Values(IrDocument doc, string name) => Assert.IsType<IrScalar>(Field(doc, name).Type).Facets?.Values;

    /// <summary>Why a field is unknown, as the IR says it.</summary>
    private static string Why(IrDocument doc, string name) =>
        Assert.Single(doc.Unknowns, u => u.Path.EndsWith("." + name, StringComparison.Ordinal)).Detail;

    [Fact]
    public void Distinct_in_an_aggregate_gives_what_the_aggregate_gives_without_it()
    {
        // Distinct_and_all_in_an_aggregate_keep_its_type_and_when_it_is_null.
        const string Calls = "COUNT({0} n) AS a, COUNT_BIG({0} v) AS b, SUM({0} v) AS c, AVG({0} d) AS d, MIN({0} s) AS e, MAX({0} status) AS f, "
            + "STDEV({0} n) AS g, STDEVP({0} f) AS h, VAR({0} v) AS i, VARP({0} n) AS j";
        foreach (var grouped in new[] { "", " GROUP BY g" })
        {
            var plain = Procedure($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "")} FROM dbo.t{grouped};");
            var distinct = Procedure($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "DISTINCT")} FROM dbo.t{grouped};");
            var all = Procedure($"SELECT {string.Format(System.Globalization.CultureInfo.InvariantCulture, Calls, "ALL")} FROM dbo.t{grouped};");
            foreach (var name in new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j" })
            {
                Assert.DoesNotContain("unknown", Typed(distinct, name), StringComparison.Ordinal);
                Assert.Equal(Typed(plain, name), Typed(distinct, name));
                Assert.Equal(Typed(plain, name), Typed(all, name));
            }
        }

        // MAX of a CHECKed column keeps its set with DISTINCT, as without.
        Assert.Equal(
            Assert.IsType<IrScalar>(Field(Procedure("SELECT MAX(status) AS f FROM dbo.t;"), "f").Type).Facets,
            Assert.IsType<IrScalar>(Field(Procedure("SELECT MAX(DISTINCT status) AS f FROM dbo.t;"), "f").Type).Facets);
    }

    [Fact]
    public void Distinct_in_an_aggregate_that_does_not_read_it_is_unknown()
    {
        // ScriptDom refuses it with OVER and in STRING_AGG, but parses it in any other call.
        var doc = Procedure("SELECT UPPER(DISTINCT s) AS a FROM dbo.t; SELECT JSON_ARRAYAGG(DISTINCT n) AS b FROM dbo.t;");
        Assert.Contains("DISTINCT in a call of UPPER", Why(doc, "a"), StringComparison.Ordinal);
        Assert.Contains("DISTINCT in a call of JSON_ARRAYAGG", Why(doc, "b"), StringComparison.Ordinal);
    }

    [Fact]
    public void Within_group_is_read_by_string_agg_and_the_percentiles_and_by_no_other_function()
    {
        // String_agg_within_group_keeps_its_type_and_when_it_is_null.
        var doc = Procedure("""
            SELECT STRING_AGG(s, ',') WITHIN GROUP (ORDER BY id DESC) AS a, STRING_AGG(s, ',') AS b,
                   PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY n) OVER () AS c, MAX(s) WITHIN GROUP (ORDER BY id) AS d
            FROM dbo.t;
            """);
        Assert.Equal(Typed(doc, "b"), Typed(doc, "a"));
        Assert.Equal("int", Typed(doc, "c"));
        Assert.Contains("WITHIN GROUP", Why(doc, "d"), StringComparison.Ordinal);
    }

    [Fact]
    public void Within_group_graph_path_is_unknown_and_a_percentile_over_it_does_not_stop_the_run()
    {
        // The server refuses a percentile over GRAPH PATH (10753); STRING_AGG's, over a MATCH's shortest path, is not observed.
        var doc = Procedure("""
            SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (GRAPH PATH) OVER () AS a, STRING_AGG(s, '->') WITHIN GROUP (GRAPH PATH) AS b
            FROM dbo.t;
            """);
        Assert.Contains("WITHIN GROUP (GRAPH PATH) in a call of PERCENTILE_CONT", Why(doc, "a"), StringComparison.Ordinal);
        Assert.Contains("WITHIN GROUP (GRAPH PATH) in a call of STRING_AGG", Why(doc, "b"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_node_type_the_typer_does_not_list_is_unknown()
    {
        // AcceptedNodes is the gate: @@ROWCOUNT's node is not in it, so no arm may type it.
        var doc = Procedure("SELECT @@ROWCOUNT AS a, n AS b FROM dbo.t;");
        Assert.Contains("is not modelled", Why(doc, "a"), StringComparison.Ordinal);
        Assert.Equal("int", Typed(doc, "b"));
    }

    [Fact]
    public void A_users_function_called_with_a_clause_is_unknown()
    {
        // ScriptDom parses dbo.f(DISTINCT x), which the server takes for a CLR aggregate.
        var doc = InferProcedure.Infer(
            "dbo.p",
            ("tables.sql", Tables),
            ("f.sql", "CREATE FUNCTION dbo.f (@x int) RETURNS int AS BEGIN RETURN @x END;\nGO\n"),
            ("p.sql", "CREATE PROCEDURE dbo.p AS\nSELECT dbo.f(DISTINCT n) AS a, dbo.f(n) AS b FROM dbo.t;"));
        Assert.Contains("This form of call is not modelled", Why(doc, "a"), StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", Typed(doc, "b"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignore_nulls_and_respect_nulls_are_unknown()
    {
        var doc = Procedure("""
            SELECT FIRST_VALUE(n) IGNORE NULLS OVER (ORDER BY id) AS a, LAG(n) RESPECT NULLS OVER (ORDER BY id) AS b, FIRST_VALUE(n) OVER (ORDER BY id) AS c
            FROM dbo.t;
            """);
        Assert.Contains("IGNORE NULLS or RESPECT NULLS", Why(doc, "a"), StringComparison.Ordinal);
        Assert.Contains("IGNORE NULLS or RESPECT NULLS", Why(doc, "b"), StringComparison.Ordinal);
        Assert.Equal("int", Typed(doc, "c"));
    }

    [Fact]
    public void Trim_from_one_side_is_typed_as_trim_from_both()
    {
        // Trim_from_one_side_keeps_the_strings_type_and_when_it_is_null.
        var doc = Procedure("""
            SELECT TRIM(LEADING 'x' FROM s) AS a, TRIM(TRAILING N'x' FROM s) AS b, TRIM(BOTH 'x' FROM ns) AS c,
                   TRIM('x' FROM s) AS d, TRIM(N'x' FROM s) AS e, TRIM('x' FROM ns) AS f, TRIM(BOTH FROM s) AS g, TRIM(s) AS h
            FROM dbo.t;
            """);
        // A side without characters, which the server refuses, ScriptDom reads as characters from a column named BOTH.
        Assert.StartsWith("unknown", Typed(doc, "g"), StringComparison.Ordinal);
        Assert.Equal("varchar(10)", Typed(doc, "h"));
        Assert.Equal(Typed(doc, "d"), Typed(doc, "a"));
        Assert.Equal(Typed(doc, "e"), Typed(doc, "b"));
        Assert.Equal(Typed(doc, "f"), Typed(doc, "c"));
        Assert.Equal("varchar(10)", Typed(doc, "a"));
    }

    [Fact]
    public void Json_value_returning_a_type_is_unknown()
    {
        // Json_value_returning_is_refused_by_this_server: whatever it would give where the server takes it, it is not the
        // call without the clause.
        var doc = Procedure("SELECT JSON_VALUE(doc, '$.a' RETURNING int) AS a, JSON_VALUE(doc, '$.a') AS b FROM dbo.t;");
        Assert.Contains("RETURNING", Why(doc, "a"), StringComparison.Ordinal);
        Assert.Equal("nvarchar(4000) json-null-or-absent", Typed(doc, "b"));
    }

    [Fact]
    public void A_json_function_reads_only_its_own_clauses()
    {
        // A_json_constructor_returning_json_is_the_constructor; json_query's array wrapper is not read. (ScriptDom refuses
        // json_objectagg's ORDER BY, which the server refuses too, and RETURNING anything but json.)
        var doc = Procedure("""
            SELECT (SELECT json_array(n RETURNING json) AS a, json_arrayagg(n ORDER BY id RETURNING json) AS b,
                           json_query(doc WITH ARRAY WRAPPER) AS d
                    FROM dbo.t FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS j;
            """);
        var why = string.Join("\n", doc.Unknowns.Select(u => u.Detail));
        Assert.Contains("WITH ARRAY WRAPPER in a call of JSON_QUERY", why, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_ARRAY ", why, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON_ARRAYAGG", why, StringComparison.Ordinal);
    }

    [Fact]
    public void Collate_keeps_a_unicode_string_and_only_the_type_of_any_other()
    {
        // Collate_keeps_a_unicode_strings_characters_and_may_change_or_cut_a_non_unicode_ones.
        var doc = Procedure("""
            SELECT ns COLLATE Greek_CI_AS AS a, s COLLATE Greek_CI_AS AS b, status COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS c, status AS c2,
                   IIF(n > 0, N'a', N'b') COLLATE Greek_CI_AS AS d, IIF(n > 0, 'a', 'b') COLLATE Greek_CI_AS AS d2,
                   n COLLATE Latin1_General_BIN AS e, (N'x') COLLATE Greek_CI_AS AS f
            FROM dbo.t;
            """);
        var aggregated = Procedure("SELECT MAX(status) AS a, MAX(status) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS b, ISNULL(status, 'open') COLLATE Greek_CI_AS AS c FROM dbo.t;");
        Assert.NotNull(Values(aggregated, "a"));
        Assert.Null(Values(aggregated, "b"));
        Assert.Null(Values(aggregated, "c"));
        Assert.Equal("nvarchar(20) nullable-column", Typed(doc, "a"));
        Assert.Equal("varchar(10)", Typed(doc, "b"));

        // A set of values holds of the characters written, which a non-Unicode collation may change; a Unicode string keeps it.
        Assert.Equal("varchar(10)", Typed(doc, "c"));
        Assert.NotNull(Values(doc, "c2"));
        Assert.Null(Values(doc, "c"));
        Assert.NotNull(Values(doc, "d"));
        Assert.Null(Values(doc, "d2"));
        Assert.Contains("COLLATE", Why(doc, "e"), StringComparison.Ordinal);
        Assert.Equal("nvarchar(1)", Typed(doc, "f"));

        // Five one-byte characters need ten bytes in UTF-8, so varchar(5) holds two of them: a CHECK's least length doesn't
        // hold under COLLATE, of a column or an aggregate of it; a Unicode string's does.
        var checkedLength = InferProcedure.Infer(
            "dbo.p",
            ("tables.sql", "CREATE TABLE dbo.w (w varchar(5) NOT NULL CHECK (LEN(w) >= 5), nw nvarchar(5) NOT NULL CHECK (LEN(nw) >= 5));\nGO\n"),
            ("p.sql", """
                CREATE PROCEDURE dbo.p AS
                SELECT w AS a, w COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS b, MAX(w) COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS c,
                       nw COLLATE Greek_CI_AS AS d
                FROM dbo.w GROUP BY w, nw;
                """));
        Assert.NotNull(Facets(checkedLength, "a")?.MinLength);
        Assert.Null(Facets(checkedLength, "b")?.MinLength);
        Assert.Null(Facets(checkedLength, "c")?.MinLength);
        Assert.NotNull(Facets(checkedLength, "d")?.MinLength);
    }
}
