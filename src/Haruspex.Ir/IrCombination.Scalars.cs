using System.Text.RegularExpressions;

namespace Haruspex.Ir;

public static partial class IrCombination
{
    private sealed partial class Combiner
    {
        /// <summary>
        /// The widest scalar both results allow, facet by facet: a length, a range, digits and a value set the larger, and a fact
        /// only one has dropped. Null when the two are values of another form, which then stand side by side in a union: another
        /// kind, or another format but integers both checked as numbers (a <c>bigint</c> in a row arrives as a string, so it is
        /// not one of them).
        /// </summary>
        /// <remarks>
        /// Inference joins facets too (<c>ScalarFacets</c> in the SQL Server front end), over its own exact decimals; this join
        /// is the IR's, for documents from any front end, and keeps a fact's source where one side's fact is kept whole.
        /// </remarks>
        private IrScalar? Widest(IrScalar was, IrScalar now)
        {
            if (was.Kind != now.Kind)
                return null;
            if (was == now)
                return now;

            var wider = now;
            if (was.Format != now.Format)
            {
                if (Rank(was.Format) is not { } before || Rank(now.Format) is not { } after)
                    return null;
                wider = before > after ? was : now;
            }

            var (old, facets) = (was.Facets ?? new IrFacets(), now.Facets ?? new IrFacets());
            var length = (old.Length, facets.Length) switch
            {
                ({ } a, { } b) when (a.Unit, a.Encoding) == (b.Unit, b.Encoding) => a.Value == b.Value && a.IsFixed == b.IsFixed ? b
                    : (a.Value > b.Value ? a : b) with { IsFixed = false },
                _ => null,
            };
            var joined = new IrFacets(
                length,
                Shortest(old, facets),
                (old.Digits, facets.Digits) switch
                {
                    ({ } a, { } b) when Holds(b, a) => b,
                    ({ } a, { } b) when Holds(a, b) => a,
                    ({ } a, { } b) => new IrDigits(Math.Max(a.Precision - a.Scale, b.Precision - b.Scale) + Math.Max(a.Scale, b.Scale), Math.Max(a.Scale, b.Scale), b.From),
                    _ => null,
                },
                Looser(old.Min, facets.Min, below: true),
                Looser(old.Max, facets.Max, below: false),
                Values(old.Values, facets.Values));

            // The SQL type kept is the one whose length or digits were kept, so that a note an emitter writes from it agrees.
            var sql = length is not null && length == old.Length && length != facets.Length ? was.Sql
                : joined.Digits is { } digits && digits == old.Digits && digits != facets.Digits ? was.Sql
                : wider.Sql;
            return wider with { Sql = sql, Facets = joined.IsEmpty ? null : joined };

            // Integers a schema checks as numbers, by the range each allows.
            static int? Rank(IrFormat? format) => format switch
            {
                IrFormat.Uint8 => 0,
                IrFormat.Int16 => 1,
                IrFormat.Int32 => 2,
                _ => null,
            };
        }

        /// <summary>
        /// The shortest length either allows, in UTF-16 code units: a fixed length is a minimum too, of the fewest code units it can
        /// hold (<see cref="IrLength.FewestUtf16Units"/>: <c>char(8)</c> of UTF-8 may be two emoji, four code units). Null when either
        /// may be as short as nothing.
        /// </summary>
        private static IrMinLength? Shortest(IrFacets was, IrFacets now)
        {
            // The one the other implies, or, of two in different units that don't imply each other, the fewer code units both hold.
            var (a, b) = (Least(was), Least(now));
            return a is null || b is null ? null
                : b.Implies(a) ? a
                : a.Implies(b) ? b
                : new IrMinLength(Math.Min(a.FewestUtf16Units, b.FewestUtf16Units), a.From);

            static IrMinLength? Least(IrFacets facets) => facets.Length is { IsFixed: true } fixedAt && (facets.MinLength is null || !facets.MinLength.Implies(new IrMinLength(fixedAt.FewestUtf16Units, fixedAt.From)))
                ? new IrMinLength(fixedAt.FewestUtf16Units, fixedAt.From)
                : facets.MinLength;
        }

        /// <summary>Whether digits <paramref name="wide"/> hold every value of <paramref name="narrow"/>: as many places, and as many before the point.</summary>
        private static bool Holds(IrDigits wide, IrDigits narrow) =>
            narrow.Scale <= wide.Scale && narrow.Precision - narrow.Scale <= wide.Precision - wide.Scale;

        /// <summary>The looser of two bounds (the lower minimum or the higher maximum), or none when either side has none.</summary>
        private static IrBound? Looser(IrBound? was, IrBound? now, bool below)
        {
            if (was is null || now is null)
                return null;
            var order = IrNumbers.Compare(was.Value, now.Value);
            if (order == 0)
                return was.Inclusive && !now.Inclusive ? was : now;
            return (order < 0) == below ? was : now;
        }

        /// <summary>
        /// Both value sets' items, where both are matched alike (the same match, and the same fold meaning the same in both
        /// documents) and are both strings or both numbers; otherwise none, claiming less. A canonical set's items are in ordinal
        /// order, as the format requires.
        /// </summary>
        private IrValueSet? Values(IrValueSet? was, IrValueSet? now)
        {
            if (was is null || now is null || was.Match != now.Match || was.Fold != now.Fold || (was.Fold is { } fold && !SameFold(fold))
                || was.Items.Any(i => i.IsString) != now.Items.Any(i => i.IsString))
            {
                return null;
            }

            var items = now.Items.Concat(was.Items).Distinct();
            return now with
            {
                Items = [.. now.Match == IrValueMatch.Canonical ? items.OrderBy(i => i.Text, StringComparer.Ordinal) : items],
                From = [.. now.From.Concat(was.From).Distinct()],
            };
        }

        /// <summary>
        /// The narrower of two parameters' types: every value of it both procedures take, so the combined function sends only
        /// that; or why there is no such type the IR can say. A parameter unknown in both stays unknown, claiming nothing, as it
        /// does in a document of one database; one known in only one can't be shown to be taken by the other.
        /// </summary>
        private static (IrType? Type, string? Why) Narrower(IrType was, IrType now) => (was, now) switch
        {
            (IrUnknownType, IrUnknownType) => (now, null),
            (IrUnknownType, _) or (_, IrUnknownType) => (null, "and haruspex doesn't model one of the two, so no value can be shown to be taken by both"),
            (IrScalar before, IrScalar after) => Narrower(before, after),
            _ => was == now ? (now, null) : (null, "which have no value in common the function could send"),
        };

        private static (IrType? Type, string? Why) Narrower(IrScalar was, IrScalar now)
        {
            const string Nothing = "which have no value in common the function could send";
            if (was == now)
                return (now, null);
            if (was.Kind != now.Kind)
                return (null, Nothing);

            if (was.Format != now.Format)
            {
                // Integers of another size: the server converts what is sent, so the narrower is taken by both (decision 0068).
                if (Rank(was.Format) is not { } before || Rank(now.Format) is not { } after)
                    return (null, Nothing);
                return (before < after ? was : now, null);
            }

            // A date or time is compared by the range and fraction of its SQL type, which the IR keeps and has no other fact for.
            if (was.Kind is IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime && was.Sql != now.Sql)
            {
                return IrDateTimes.Takes(now.Sql, was.Sql) ? (was, null)
                    : IrDateTimes.Takes(was.Sql, now.Sql) ? (now, null)
                    : (null, "neither of whose range and fraction holds the other's, so no one type of the two sends only what both take");
            }

            var (old, facets) = (was.Facets ?? new IrFacets(), now.Facets ?? new IrFacets());
            if (old.Length is { } a && facets.Length is { } b && (a.Unit, a.Encoding) != (b.Unit, b.Encoding))
                return (null, "whose lengths are counted in another unit, so what one takes can't be shown to be taken by the other (a character outside a code page is changed without an error)");

            var length = (old.Length, facets.Length) switch
            {
                ({ } x, { } y) => x.Value < y.Value ? x : y,
                (var x, var y) => x ?? y,
            };
            var (digits, sql) = Digits(was, now);
            if (digits is null && old.Digits is not null && facets.Digits is not null)
                return (null, Nothing);

            var min = Tighter(old.Min, facets.Min, above: true);
            var max = Tighter(old.Max, facets.Max, above: false);
            if (min is not null && max is not null && IrNumbers.Compare(min.Value, max.Value) is var order && (order > 0 || (order == 0 && !(min.Inclusive && max.Inclusive))))
                return (null, "whose ranges don't meet");

            var values = (old.Values, facets.Values) switch
            {
                ({ } x, { } y) when x.Match == y.Match && x.Fold == y.Fold => y with { Items = [.. y.Items.Intersect(x.Items)] },
                ({ }, { }) => null,
                (var x, var y) => x ?? y,
            };
            if (old.Values is not null && facets.Values is not null && values is not { Items.Count: > 0 })
                return (null, "whose value sets have no value in common");

            var narrowed = new IrFacets(length, Longest(old.MinLength, facets.MinLength), digits, min, max, values);

            // The SQL type the function declares is the one whose length or digits were kept: a query file sends it to sp_executesql.
            sql ??= length is not null && length == old.Length && length != facets.Length ? was.Sql : now.Sql;
            return (now with { Sql = sql, Facets = narrowed.IsEmpty ? null : narrowed }, null);

            // Both hold, so either may be kept: the one that implies the other, or the first.
            static IrMinLength? Longest(IrMinLength? x, IrMinLength? y) => x is null ? y : y is null ? x : y.Implies(x) ? y : x;

            static int? Rank(IrFormat? format) => format switch
            {
                IrFormat.Uint8 => 0,
                IrFormat.Int16 => 1,
                IrFormat.Int32 => 2,
                IrFormat.Int64 => 3,
                _ => null,
            };
        }

        /// <summary>
        /// The digits both decimals take, and the SQL type that declares them when it is neither's: the fewer places before the
        /// point and the fewer after, so <c>decimal(9,3)</c> and <c>decimal(9,2)</c> meet at <c>decimal(8,2)</c>, which each takes
        /// exactly. Null digits when either has none; null SQL when one side's digits are kept whole, whose SQL type is used.
        /// </summary>
        private static (IrDigits? Digits, string? Sql) Digits(IrScalar was, IrScalar now)
        {
            if (was.Facets?.Digits is not { } a || now.Facets?.Digits is not { } b)
                return (was.Facets?.Digits ?? now.Facets?.Digits, null);
            if (Holds(a, b))
                return (b, now.Sql);
            if (Holds(b, a))
                return (a, was.Sql);

            var scale = Math.Min(a.Scale, b.Scale);
            var precision = Math.Min(a.Precision - a.Scale, b.Precision - b.Scale) + scale;
            return DecimalSpelling().Match(now.Sql ?? "") is { Success: true } spelt
                ? (new IrDigits(precision, scale, new IrFactSource(IrFactSourceKind.Type, $"{spelt.Groups["name"].Value}({precision},{scale})")), $"{spelt.Groups["name"].Value}({precision},{scale})")
                : (null, null);
        }

        /// <summary>The tighter of two bounds (the higher minimum or the lower maximum), or the one there is.</summary>
        private static IrBound? Tighter(IrBound? was, IrBound? now, bool above)
        {
            if (was is null || now is null)
                return was ?? now;
            var order = IrNumbers.Compare(was.Value, now.Value);
            if (order == 0)
                return was.Inclusive ? now : was;
            return (order > 0) == above ? was : now;
        }

        /// <summary>A decimal type as SQL Server spells it: its name, and its precision and scale.</summary>
        [GeneratedRegex(@"^(?<name>decimal|numeric)\(\d+,\s*\d+\)$", RegexOptions.CultureInvariant)]
        private static partial Regex DecimalSpelling();
    }
}
