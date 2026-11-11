using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// The numeric expressions haruspex types: decimal arithmetic beyond 38 digits and <c>%</c>, a bit beside a number, division by
/// a literal, <c>AVG</c>, the statistical aggregates and the numeric functions. Each type and each reason to be null rests on an
/// observation in the differential tests' <c>NumericObservationTests</c>, named where it matters; <c>ArithmeticGridTests</c>
/// checks the precision rules against the server over a grid.
/// </summary>
public class NumericTests
{
    private const string Tables = """
        CREATE TABLE dbo.t (
            id int NOT NULL PRIMARY KEY, g int NOT NULL, i int NOT NULL, v int NULL, ti tinyint NOT NULL, si smallint NOT NULL, bi bigint NOT NULL,
            bt bit NOT NULL, d decimal(10,2) NOT NULL, d192 decimal(19,2) NOT NULL, d196 decimal(19,6) NULL, d380 decimal(38,0) NOT NULL,
            d3838 decimal(38,38) NOT NULL, d3810 decimal(38,10) NOT NULL, nd numeric(10,2) NULL, d55 decimal(5,5) NOT NULL, d42 decimal(4,2) NOT NULL,
            m money NOT NULL, sm smallmoney NOT NULL, f float NOT NULL, r real NOT NULL);
        GO
        """;

    private static IrDocument Procedure(string body) =>
        InferProcedure.Infer("dbo.p", ("tables.sql", Tables), ("p.sql", $"CREATE PROCEDURE dbo.p AS\n{body}"));

    // Stricter than IrNavigation.Fields: asserts one result, an array of rows.
    private static IReadOnlyList<IrField> Fields(IrDocument doc)
    {
        var results = doc.Objects[0].Results ?? throw new InvalidOperationException("The results are unknown: " + string.Join("; ", doc.Unknowns.Select(u => u.Detail)));
        var row = (IrRef)((IrArray)Assert.Single(results).Type).Items;
        return doc.Types[row.Name].Fields;
    }

    /// <summary>Each field as <c>name: sql reasons…</c>, or <c>name: unknown</c>, one per line.</summary>
    private static string Typed(string body)
    {
        var doc = Procedure(body);
        return string.Join("\n", Fields(doc).Select(f => f.Name + ": " + (f.Type is IrScalar scalar
            ? string.Join(" ", [scalar.Sql ?? "(none)", .. f.NullableBecause.Select(r => r.Rule)])
            : "unknown")));
    }

    // ---- decimal arithmetic -------------------------------------------------------------------------------------------

    [Fact]
    public void Decimal_arithmetic_past_38_digits_is_capped_and_may_overflow_only_where_its_integer_digits_do_not_fit()
    {
        // Decimal_arithmetic_beyond_38_digits_caps_the_precision_and_cuts_the_scale;
        // Decimal_overflow_and_division_by_zero_are_errors_by_default_and_null_under_the_callers_options;
        // Decimal_arithmetic_whose_integer_digits_fit_cannot_overflow_at_its_operands_extremes.
        Assert.Equal(
            """
            a: numeric(22,3)
            b: decimal(38,7) nullable-column
            c: decimal(38,0) arithmetic-overflow
            e: decimal(38,37)
            h: decimal(38,6) arithmetic-overflow
            k: decimal(38,0) arithmetic-overflow
            l: decimal(38,10) arithmetic-overflow
            n: decimal(38,0) arithmetic-overflow
            o: numeric(38,1) arithmetic-overflow
            p: decimal(38,38) arithmetic-overflow
            q: decimal(38,6) arithmetic-overflow
            s: decimal(38,6) arithmetic-overflow
            t: decimal(11,2)
            u: decimal(14,2)
            w: decimal(38,0)
            """,
            Typed("""
                SELECT -1.0 * d192 AS a, d192 * d196 AS b, d380 * d380 AS c, d3838 * d3838 AS e, d380 / d3838 AS h, d380 + d380 AS k,
                       d3810 + d3810 AS l, d3810 + d380 AS n, -1.0 * d380 AS o, d3838 + d3838 AS p, d3810 / d3810 AS q, d3810 * d3810 AS s,
                       d + d AS t, d * 100 AS u, -d380 AS w
                FROM dbo.t;
                """));
    }

    [Fact]
    public void Division_and_modulo_by_a_literal_of_at_least_1_cannot_divide_by_zero_or_overflow()
    {
        // Decimal_arithmetic_whose_integer_digits_fit_cannot_overflow_at_its_operands_extremes;
        // Integer_and_money_division_overflow_only_at_the_least_value_divided_by_minus_one;
        // Modulo_has_the_documented_precision_and_scale_and_integers_and_money_keep_their_type.
        Assert.Equal(
            """
            a: decimal(14,6)
            b: decimal(21,13) arithmetic-overflow
            c: decimal(3,2)
            e: decimal(10,2) arithmetic-overflow
            g: int
            h: int
            k: int nullable-column arithmetic-overflow
            l: int arithmetic-overflow
            n: int arithmetic-overflow
            o: money
            q: float
            s: float arithmetic-overflow
            t: money
            u: money arithmetic-overflow
            w: decimal(38,38) arithmetic-overflow
            x: smallmoney
            y: decimal(38,6) arithmetic-overflow
            z: numeric(15,6)
            """,
            Typed("""
                SELECT d / 100 AS a, d / i AS b, d % 7 AS c, d % i AS e, i % 3 AS g, i / 2 AS h, i / v AS k, i % -1 AS l, i / 0 AS n,
                       m / 4 AS o, f / 2 AS q, f / 0.5 AS s, m % 2 AS t, m % m AS u, d380 % d3838 AS w, sm % 2 AS x,
                       d380 / 10 AS y, d / 10.0 AS z
                FROM dbo.t;
                """));
    }

    [Fact]
    public void A_bit_beside_a_number_takes_its_type_and_float_modulo_and_two_bits_are_unknown()
    {
        // A_bit_beside_a_number_takes_the_numbers_type_and_two_bits_are_an_error;
        // Modulo_has_the_documented_precision_and_scale_and_integers_and_money_keep_their_type (float % is 402).
        Assert.Equal(
            """
            a: decimal(21,4)
            b: decimal(11,2)
            c: decimal(23,13) arithmetic-overflow
            e: tinyint arithmetic-overflow
            g: int arithmetic-overflow
            h: money arithmetic-overflow
            k: float arithmetic-overflow
            l: decimal(38,37) arithmetic-overflow
            n: unknown
            o: unknown
            q: unknown
            """,
            Typed("""
                SELECT bt * d AS a, bt + d AS b, d / bt AS c, ti + bt AS e, bt + 2 AS g, bt + m AS h, f + bt AS k, bt * d3838 AS l,
                       bt + bt AS n, f % 2 AS o, d % f AS q
                FROM dbo.t;
                """));
    }

    [Fact]
    public void Modulo_overflows_where_an_operand_brought_to_its_scale_passes_38_digits()
    {
        // Modulo_brings_both_operands_to_its_scale_first_which_can_overflow.
        Assert.Equal(
            """
            a: numeric(2,1) arithmetic-overflow
            b: decimal(1,0)
            c: decimal(38,38) arithmetic-overflow
            e: decimal(5,5)
            g: numeric(38,38) arithmetic-overflow
            """,
            Typed("SELECT d380 % 1.0 a, d380 % 7 b, d3838 % 1 c, d55 % 1 e, d3838 % 7.5 g FROM dbo.t;"));
    }

    [Fact]
    public void A_bit_beside_a_decimal_of_places_only_may_overflow()
    {
        // A_bit_converted_to_a_decimal_of_places_only_overflows_when_it_is_1.
        Assert.Equal(
            """
            a: decimal(6,5) arithmetic-overflow
            b: decimal(6,5) arithmetic-overflow
            c: decimal(11,10) arithmetic-overflow
            e: decimal(38,37) arithmetic-overflow
            g: decimal(11,2)
            h: decimal(9,4)
            """,
            Typed("SELECT d55 + bt a, bt - d55 b, d55 * bt c, d3838 * bt e, d + bt g, d42 * bt h FROM dbo.t;"));
    }

    // ---- AVG and the statistical aggregates ---------------------------------------------------------------------------

    [Fact]
    public void Avg_is_typed_by_its_argument_and_null_over_no_rows_and_overflows_as_sum_does()
    {
        // Avg_is_typed_by_its_argument_with_or_without_a_window; Avg_is_null_over_no_rows_and_overflows_as_sum_does.
        Assert.Equal(
            """
            a: int empty-aggregate arithmetic-overflow
            b: int empty-aggregate arithmetic-overflow
            c: bigint empty-aggregate arithmetic-overflow
            e: decimal(38,6) empty-aggregate
            h: decimal(38,6) empty-aggregate arithmetic-overflow
            j: decimal(38,38) empty-aggregate arithmetic-overflow
            k: numeric(38,6) nullable-column empty-aggregate
            l: money empty-aggregate arithmetic-overflow
            n: money empty-aggregate arithmetic-overflow
            o: float empty-aggregate arithmetic-overflow
            q: float empty-aggregate arithmetic-overflow
            s: decimal(38,6) empty-aggregate
            """,
            Typed("""
                SELECT AVG(ti) a, AVG(i) b, AVG(bi) c, AVG(d) e, AVG(d380) h, AVG(d3838) j, AVG(nd) k, AVG(m) l, AVG(sm) n, AVG(f) o, AVG(r) q,
                       AVG(DISTINCT d192) s
                FROM dbo.t;
                """));
        Assert.Equal(
            """
            g: int
            a: decimal(38,6)
            b: numeric(38,6) nullable-column
            c: decimal(38,6)
            """,
            Typed("SELECT g, AVG(d) a, AVG(nd) b, AVG(d192) c FROM dbo.t GROUP BY g HAVING AVG(d192) > 0;"));
        Assert.Equal("a: unknown", Typed("SELECT AVG(bt) a FROM dbo.t;"));
    }

    [Fact]
    public void Avg_over_a_window_is_typed_as_without_one()
    {
        // Avg_is_typed_by_its_argument_with_or_without_a_window; a frame that may leave out the current row can be empty.
        Assert.Equal(
            """
            a: decimal(38,6)
            b: int arithmetic-overflow
            c: decimal(38,6) empty-frame
            e: numeric(38,6) nullable-column
            """,
            Typed("""
                SELECT AVG(d) OVER (PARTITION BY g) a, AVG(i) OVER () b, AVG(d) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) c,
                       AVG(nd) OVER () e
                FROM dbo.t;
                """));
    }

    [Fact]
    public void The_statistical_aggregates_are_float_and_the_sample_forms_need_two_values()
    {
        // The_statistical_aggregates_are_float_and_the_sample_forms_null_over_fewer_than_two_values;
        // The_statistical_aggregates_overflow_only_over_float.
        Assert.Equal(
            """
            a: float empty-aggregate too-few-values
            b: float empty-aggregate
            c: float empty-aggregate too-few-values
            e: float empty-aggregate arithmetic-overflow
            h: float nullable-column empty-aggregate
            k: float empty-aggregate too-few-values
            """,
            Typed("SELECT STDEV(i) a, STDEVP(d) b, VAR(m) c, VARP(f) e, VARP(nd) h, STDEV(r) k FROM dbo.t;"));
        Assert.Equal(
            """
            g: int
            a: float too-few-values
            b: float
            c: float
            """,
            Typed("SELECT g, STDEV(d) a, VARP(d) b, VAR(d380) c FROM dbo.t GROUP BY g HAVING VAR(d380) > 0;"));
        Assert.Equal(
            """
            a: float too-few-values
            b: float arithmetic-overflow
            c: float empty-frame
            """,
            Typed("SELECT STDEV(d) OVER (PARTITION BY g) a, VARP(f) OVER () b, STDEVP(d) OVER (ORDER BY id ROWS BETWEEN 2 PRECEDING AND 1 PRECEDING) c FROM dbo.t;"));
        Assert.Equal("a: unknown", Typed("SELECT STDEV(bt) a FROM dbo.t;"));
    }

    // ---- numeric functions --------------------------------------------------------------------------------------------

    [Fact]
    public void Round_is_typed_by_its_argument_and_overflows_only_when_it_may_carry()
    {
        // Round_floor_ceiling_abs_and_sign_are_typed_by_their_argument;
        // Round_overflows_when_it_carries_into_a_digit_its_type_lacks_unless_it_truncates.
        Assert.Equal(
            """
            a: decimal(10,2) arithmetic-overflow
            b: decimal(10,2)
            c: decimal(10,2)
            e: decimal(10,2)
            g: decimal(10,2) nullable-column arithmetic-overflow
            h: int
            k: int arithmetic-overflow
            l: int
            n: money
            o: money
            q: money
            s: float
            t: float arithmetic-overflow
            u: float
            w: float
            x: numeric(2,1) arithmetic-overflow
            y: decimal(10,2) arithmetic-overflow
            z: bigint arithmetic-overflow
            aa: money arithmetic-overflow
            """,
            Typed("""
                SELECT ROUND(d, 1) a, ROUND(d, 2) b, ROUND(d, 5) c, ROUND(d, 0, 1) e, ROUND(d, v) g, ROUND(ti, -1) h, ROUND(i, -1) k, ROUND(i, 0) l,
                       ROUND(m, 2) n, ROUND(m, 4) o, ROUND(sm, 0) q, ROUND(f, 2) s, ROUND(f, -2) t, ROUND(r, -2) u, ROUND(bt, 0) w, ROUND(9.5, 0) x,
                       ROUND(d, 1, v) y, ROUND(bi, -2) z, ROUND(m, 3) aa
                FROM dbo.t;
                """));
    }

    [Fact]
    public void Floor_ceiling_abs_and_sign_are_typed_by_their_argument_and_overflow_only_at_an_edge_their_type_lacks()
    {
        // Round_floor_ceiling_abs_and_sign_are_typed_by_their_argument;
        // Floor_ceiling_abs_and_sign_overflow_only_where_the_type_cannot_hold_the_result.
        Assert.Equal(
            """
            a: decimal(10,0)
            b: decimal(5,0)
            c: numeric(10,0) nullable-column
            e: money arithmetic-overflow
            g: money
            h: int
            k: bigint
            l: float
            n: int arithmetic-overflow
            o: int
            q: int
            s: bigint arithmetic-overflow
            t: decimal(10,2)
            u: money arithmetic-overflow
            w: money
            x: float
            """,
            Typed("""
                SELECT FLOOR(d) a, CEILING(d55) b, FLOOR(nd) c, FLOOR(m) e, CEILING(sm) g, FLOOR(ti) h, CEILING(bi) k, FLOOR(f) l,
                       ABS(i) n, ABS(si) o, ABS(ti) q, ABS(bi) s, ABS(d) t, ABS(m) u, ABS(sm) w, ABS(r) x
                FROM dbo.t;
                """));
        Assert.Equal(
            """
            a: decimal(6,5)
            b: decimal(38,37)
            c: int
            e: int
            g: money
            h: float
            k: decimal(10,2)
            l: int nullable-column
            """,
            Typed("SELECT SIGN(d55) a, SIGN(d3838) b, SIGN(i) c, SIGN(si) e, SIGN(m) g, SIGN(f) h, SIGN(d) k, SIGN(v) l FROM dbo.t;"));
    }

    [Fact]
    public void Power_is_typed_by_its_first_argument_and_overflows_unless_a_whole_exponent_keeps_it_within_its_type()
    {
        // Power_is_typed_by_its_first_argument_and_the_others_are_float;
        // Power_computes_in_float_and_overflows_or_divides_by_zero_as_its_type_allows.
        Assert.Equal(
            """
            a: decimal(38,2)
            b: decimal(38,0) arithmetic-overflow
            c: decimal(38,2) arithmetic-overflow domain-error
            e: int
            g: int arithmetic-overflow
            h: int arithmetic-overflow
            k: float arithmetic-overflow
            l: decimal(38,2) arithmetic-overflow
            n: decimal(38,2) arithmetic-overflow domain-error
            o: int nullable-column arithmetic-overflow
            q: money arithmetic-overflow
            s: decimal(38,2)
            t: float
            u: decimal(38,2) arithmetic-overflow
            """,
            Typed("""
                SELECT POWER(d, 2) a, POWER(d380, 2) b, POWER(d, f) c, POWER(ti, 3) e, POWER(ti, 4) g, POWER(i, 2) h, POWER(f, 2) k,
                       POWER(d, -1) l, POWER(d, 0.5) n, POWER(v, 2) o, POWER(sm, 2) q, POWER(d42, 17) s, POWER(bt, 2) t, POWER(d42, 18) u
                FROM dbo.t;
                """));
    }

    [Fact]
    public void Sqrt_square_log_and_exp_are_float_and_null_under_the_callers_options_outside_their_domain()
    {
        // Power_is_typed_by_its_first_argument_and_the_others_are_float;
        // A_math_function_outside_its_domain_is_an_error_by_default_and_null_under_the_callers_options.
        Assert.Equal(
            """
            a: float domain-error
            b: float
            c: float
            e: float
            g: float arithmetic-overflow
            h: float
            k: float domain-error
            l: float domain-error
            n: float domain-error
            o: float arithmetic-overflow
            q: float
            s: float
            t: float arithmetic-overflow
            u: float nullable-column arithmetic-overflow
            w: float nullable-column domain-error
            """,
            Typed("""
                SELECT SQRT(d) a, SQRT(ti) b, SQRT(bt) c, SQUARE(d380) e, SQUARE(f) g, SQUARE(r) h, LOG(d) k, LOG(i, 2) l, LOG10(m) n,
                       EXP(d) o, EXP(ti) q, EXP(d42) s, EXP(f) t, EXP(v) u, LOG(f, v) w
                FROM dbo.t;
                """));
    }

    [Fact]
    public void A_numeric_function_reads_only_its_arguments_so_coalesce_reads_it_once()
    {
        // COALESCE reads an argument twice only when it may give another value; these functions read only their arguments.
        Assert.Equal(
            """
            a: numeric(10,2)
            b: float
            """,
            Typed("SELECT COALESCE(ROUND(nd, 1), 0) a, COALESCE(SQRT(v), 0) b FROM dbo.t;"));
    }
}
