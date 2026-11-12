namespace Haruspex.SqlServer.Inference.Typing;

/// <summary>
/// The server's type rule for a chain of values that meet in one column: a set operation's, <c>COALESCE</c>'s, a
/// <c>CASE</c>'s, <c>IIF</c>'s and <c>CHOOSE</c>'s. README.md in the project ("Combining types") gives the rule and the
/// observations it rests on.
/// </summary>
internal static class TypeChain
{
    /// <summary>
    /// The server's type rule for a chain: the type a column takes whose values are of these types, as a set operation,
    /// <c>COALESCE</c>, <c>CASE</c>, <c>IIF</c> and <c>CHOOSE</c> all type it (one rule; observed over every ordered pair of a grid, SetTypeGridTests, and in
    /// SetTypeObservationTests), or null when SQL Server refuses them (<see cref="Clash"/>). It is a fold, left to right, of
    /// <see cref="Pair"/> (observed: A_chain_is_typed_left_to_right_so_money_absorbs_an_integer_before_a_decimal): bigint, then
    /// money, then decimal(5,2) is decimal(19,4), money having taken the bigint in; bigint, decimal(5,2), money is
    /// decimal(23,4). The date and time types are the exception: their fractional digits are the most over every value
    /// (observed: A_chain_takes_the_most_fractional_digits_of_every_date_and_time). Whether each value converts to the result
    /// without an overflow is the caller's question (<see cref="Conversions.Converts"/>).
    /// <para>
    /// It is not a join. A join is associative and commutative, and the fold is neither: money between bigint and decimal(5,2)
    /// changes the result. So it is never used to say what is known where paths meet (<see cref="Value.Join"/>), and nothing
    /// regroups or reorders a chain's types before folding them.
    /// </para>
    /// </summary>
    public static ScalarType? Of(IReadOnlyList<Operand> types) => Clash(types) is null ? Fold(types) : null;

    private static ScalarType? Fold(IReadOnlyList<Operand> operands)
    {
        if (operands.Count == 0)
            return null;
        var types = operands.Select(o => o.Type).ToList();
        static int Fraction(ScalarType t) => t.Name switch
        {
            "datetimeoffset" or "datetime2" or "time" => t.Precision,
            "datetime" => 3,
            _ => 0,
        };

        // The chain's result is a type: the digits its operands counted were the operands', and stay with them.
        var folded = operands.Skip(1).Aggregate(operands[0], Pair).Type;
        return folded.Name switch
        {
            "datetimeoffset" or "datetime2" => folded with { Precision = types.Max(Fraction) },
            "time" => folded with { Precision = types.Where(t => t.Name == "time").Max(t => t.Precision) },
            _ => folded,
        };
    }

    /// <summary>
    /// Two types combined, as SQL Server does at each step of a chain: the one of higher precedence (<see cref="ScalarType.PrecedenceRank"/>),
    /// whatever the range, with
    /// <list type="bullet">
    /// <item>two integers the wider, counting the more digits (an integer literal its own);</item>
    /// <item>an integer and money or smallmoney that money type, its digits only (15 or 6): the integer's are gone;</item>
    /// <item>a decimal and an exact number a decimal holding both's integer digits and the larger scale, past 38 digits the
    /// scale cut to 38 less those digits with no floor, named as the first decimal; bit beside it adds nothing;</item>
    /// <item>two strings national if either is, varying if either is, the longer (cut at 4000 for national), max if either is.</item>
    /// </list>
    /// A pair SQL Server would refuse still gives the higher one: a chain is refused only when a value cannot become the
    /// final type (<see cref="Clash"/>; observed: A_chain_is_refused_only_when_a_value_cannot_become_its_type).
    /// </summary>
    /// <remarks>
    /// Each case names the digits its result counts as the next step's operand: two integers the more of both's, an integer
    /// beside a bit its own, anything else its type's (<see cref="Operand.OfType"/>).
    /// </remarks>
    private static Operand Pair(Operand x, Operand y)
    {
        var (a, b) = (x.Type, y.Type);
        static bool Exact(ScalarType t) => t.IsInteger || t.IsDecimal || t.IsMoney;
        if ((a.IsDecimal || b.IsDecimal) && Exact(a) && Exact(b))
        {
            var (pa, sa) = x.AsDecimal();
            var (pb, sb) = y.AsDecimal();
            var integer = Math.Max(pa - sa, pb - sb);
            var scale = Math.Min(Math.Max(sa, sb), 38 - integer);
            return Operand.OfType(new ScalarType(a.IsDecimal ? a.Name : b.Name, Precision: integer + scale, Scale: scale));
        }

        if (a.IsInteger && b.IsInteger)
        {
            var wider = ScalarType.PrecedenceRank(a.Name) >= ScalarType.PrecedenceRank(b.Name) ? a : b;
            var digits = Math.Max(x.AsDecimal().Precision, y.AsDecimal().Precision);
            return new Operand(wider, digits == ScalarType.IntegerDigits(wider.Name) ? null : digits);
        }

        if (a.IsString && b.IsString)
        {
            var varying = a.Name.EndsWith("varchar", StringComparison.Ordinal) || b.Name.EndsWith("varchar", StringComparison.Ordinal);
            return Operand.OfType(ScalarType.String(a.IsNational || b.IsNational, varying, a.IsMax || b.IsMax ? -1 : Math.Max(a.Length, b.Length)));
        }

        // bit beside an integer adds nothing: an integer literal keeps its digits (observed over the grid's triples).
        var top = ScalarType.PrecedenceRank(a.Name) >= ScalarType.PrecedenceRank(b.Name) ? x : y;
        return top.Type.IsDecimal || top.Type.IsInteger ? top : Operand.OfType(top.Type);
    }

    /// <summary>
    /// A value SQL Server refuses to convert to the type the chain folds to, and a value of that type (or the type itself), in
    /// the order they come; null when every value converts (observed:
    /// Types_without_an_implicit_conversion_are_refused_with_an_operand_type_clash, and every pair in SetTypeGridTests; for a
    /// chain, A_chain_is_refused_only_when_a_value_cannot_become_its_type: 1, a date, a datetime is datetime, though 1 and a
    /// date alone are refused). Refused: uniqueidentifier with anything but a string; date, time, datetime2 and datetimeoffset
    /// with a number or bit; date with time.
    /// </summary>
    public static (ScalarType First, ScalarType Second)? Clash(IReadOnlyList<Operand> operands)
    {
        var types = operands.Select(o => o.Type).ToList();
        static bool Numeric(ScalarType t) => t.IsNumber || t.Name == "bit";
        static bool Refused(ScalarType a, ScalarType b) =>
            (a.Name == "uniqueidentifier" && b.Name != "uniqueidentifier" && !b.IsString)
            || (a.Name is "date" or "time" or "datetime2" or "datetimeoffset" && Numeric(b))
            || (a.Name == "date" && b.Name == "time");

        if (Fold(operands) is not { } final)
            return null;
        var own = types.FindIndex(t => t.Name == final.Name);
        for (var i = 0; i < types.Count; i++)
        {
            if (!Refused(types[i], final) && !Refused(final, types[i]))
                continue;
            if (own < 0)
                return (types[i], final);
            return own < i ? (types[own], types[i]) : (types[i], types[own]);
        }

        return null;
    }
}
