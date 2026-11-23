using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Two documents combined into one whose types hold against both databases: the types an application is generated with while
/// it may meet the database before a change is deployed (the baseline) or after it (the candidate). <c>haruspex generate</c>
/// with a baseline writes it, and every emitter writes its code from it as from any document.
/// </summary>
/// <remarks>
/// <para>
/// Values flow two ways, and each is combined in its own direction, as <see cref="IrFit"/> judges them. <b>Results</b> flow from
/// the database, so they <b>widen</b>: each takes the widest type both allow, merged rather than given as alternatives, so
/// that one object type covers both. A key only one returns may be left out (<see cref="IrPresence.Optional"/>), a value is
/// nullable if either's is, a length, a range and a value set are the larger, a trailing result set only one returns may be
/// missing (<see cref="IrNullRule.NotInBoth"/>), and a value of another kind is a union of both. <b>Parameters</b> flow to the
/// database, so they <b>narrow</b>: the combined function sends only what both procedures take. A parameter only one has is
/// left out while it has a default there; a type is the narrower of the two; an <c>OUTPUT</c> parameter's value comes back too,
/// so one in both is bridged only when its type is the same in both.
/// </para>
/// <para>
/// What no one function bridges is reported (<see cref="IrCombinationKind.Unbridgeable"/>) and its object left out, so that
/// one stuck procedure doesn't stop the rest: a new required parameter, a removed one the baseline requires, types with no
/// value in common, an <c>OUTPUT</c> parameter of another type, a result read another way, a query file whose text changed or
/// names what the baseline's database lacks. So is an object only one document has, which the other database can't run. A key gone and another appeared in the same object type is reported as a possible rename.
/// </para>
/// <para>
/// The combined document is then held to the fits-within operation itself: an application built against it must meet each
/// database with nothing refused. Whatever is (a case the combining rules don't foresee) is reported as unbridgeable and its
/// object left out too, so the document never claims a fit the operation can't show. A parameter the combined document has as
/// unknown is the one exception: it claims nothing, as in a document of one database. Why each rule is as it is:
/// docs/decisions/0073-generate-with-a-baseline-writes-types-that-hold-against-both-databases.md.
/// </para>
/// </remarks>
public static partial class IrCombination
{
    /// <summary>
    /// <paramref name="baseline"/> and <paramref name="candidate"/> combined, with what combining them found. The combined
    /// document is the candidate's but for its types: its objects' names, places and texts, its overrides, and its source, which
    /// names the baseline's.
    /// </summary>
    /// <param name="baseline">The database the application may meet before the change is deployed.</param>
    /// <param name="candidate">The database it may meet after.</param>
    /// <param name="options">How the application's generated code reacts to what it doesn't know, as the fits-within check of the result asks.</param>
    public static IrCombined Of(IrDocument baseline, IrDocument candidate, IrFitOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.Source.Baseline is not null)
            throw new ArgumentException("A baseline is a document of one database: a combined document describes an application, not a database.", nameof(baseline));
        if (candidate.Source.Baseline is not null)
            throw new ArgumentException("A candidate is a document of one database.", nameof(candidate));

        var combined = new Combiner(baseline, candidate, options, []).Combined();
        var unproven = Unproven(combined.Document, baseline, candidate, options);
        return unproven.Count == 0 ? combined : new Combiner(baseline, candidate, options, unproven).Combined();
    }

    /// <summary>
    /// What the fits-within operation refuses of an application built against the combined document meeting either database:
    /// each said as unbridgeable, so that its object is left out. Empty whenever the combining rules hold, as the tests require.
    /// </summary>
    private static List<IrCombinationFinding> Unproven(IrDocument combined, IrDocument baseline, IrDocument candidate, IrFitOptions options)
    {
        var unknownParameters = combined.Objects
            .SelectMany(o => o.Parameters.Where(p => p.Type is IrUnknownType).Select(p => (o.Id, IrPath.Render([new IrPathKey("parameters"), new IrPathKey(p.Name)]))))
            .ToHashSet();
        return
        [
            .. new[] { (Database: baseline, Which: "the baseline's"), (Database: candidate, Which: "the candidate's") }
                .SelectMany(side => IrFit.Of(combined, side.Database, options)
                    .Where(f => f.Verdict == IrFitVerdict.Refused && !unknownParameters.Contains((f.Change.Object, f.Change.Path)))
                    .Select(f => new IrCombinationFinding(IrCombinationKind.Unbridgeable, f.Change.Object, f.Change.Path,
                        $"its combined types can't be shown to work against {side.Which} database ({f.Change.Description}, and {f.Why}): ship the change in two releases"))),
        ];
    }

    /// <summary>
    /// One combining of the two documents, leaving out the objects <paramref name="unproven"/> names. It holds what is built up as
    /// the objects are combined: the findings, and for each object type of the combined document, the baseline's types it must
    /// also accept (decision 0073); both live only as long as the combining.
    /// </summary>
    private sealed partial class Combiner(IrDocument baseline, IrDocument candidate, IrFitOptions options, IReadOnlyList<IrCombinationFinding> unproven)
    {
        private readonly List<IrCombinationFinding> findings = [];

        /// <summary>The combined document and its findings.</summary>
        public IrCombined Combined()
        {
            var before = baseline.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
            var leftOut = unproven.Select(f => f.Object).ToHashSet(StringComparer.Ordinal);
            var objects = new List<IrObject>();
            foreach (var now in candidate.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (leftOut.Contains(now.Id))
                {
                    findings.AddRange(unproven.Where(f => f.Object == now.Id).Distinct());
                    continue;
                }

                if (!before.TryGetValue(now.Id, out var was))
                {
                    // A query file is the application's own SQL, so it is never missing from the baseline's database; what isn't
                    // known is what its text returns from it, which only a baseline read with that text says.
                    if (now.Kind == IrObjectKind.Query)
                        Unbridgeable(now.Id, "", "isn't in the baseline, so what it returns from the baseline's database isn't known: give the baseline as a source, so that this text is read against it");
                    else
                        findings.Add(new IrCombinationFinding(IrCombinationKind.OnlyInCandidate, now.Id, "", "the baseline's database doesn't have it, so the application can't call it until the change is deployed"));
                    continue;
                }

                if (Object(was, now) is { } combined)
                    objects.Add(combined);
            }

            // A query file only the baseline has is gone from the application, which no longer sends it.
            foreach (var gone in baseline.Objects.Where(o => o.Kind == IrObjectKind.Procedure && !candidate.Objects.Any(n => n.Id == o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
                findings.Add(new IrCombinationFinding(IrCombinationKind.OnlyInBaseline, gone.Id, "", "the candidate's database doesn't have it, so the application can't call it once the change is deployed"));

            var types = Types(objects);
            var kept = objects.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
            var document = new IrDocument(
                candidate.Source with { Baseline = baseline.Source },
                [.. objects],
                types,
                Unknowns(objects))
            {
                Overrides = [.. candidate.Overrides.Where(o => kept.Contains(o.Object))],
                TableTypes = TableTypes(objects),

                // Both documents are read with one config file, so the candidate's declarations are the application's.
                Columns = candidate.Columns,
            };
            document = document with { Folds = Folds(document) };
            return new IrCombined(document, [.. findings.Distinct().OrderBy(f => f.Object, StringComparer.Ordinal)]);
        }

        /// <summary>One object both documents have, combined; null when a change can't be bridged, which is reported.</summary>
        private IrObject? Object(IrObject was, IrObject now)
        {
            if (was.Kind != now.Kind)
                return Unbridgeable(now.Id, "", $"changed from a {IrWireNames.Of(was.Kind)} to a {IrWireNames.Of(now.Kind)}: ship the change in two releases");

            // The baseline holds what the old text returned, and the application sends the new text: neither says what the new
            // text returns from the old database. A baseline read from a source is read with the text as it is now.
            if (now.Kind == IrObjectKind.Query && was.Text != now.Text)
                return Unbridgeable(now.Id, "", "its text changed since the baseline, which says what the old text returns, not the new: give the baseline as a source, so that this text is read against it");

            // A query file's text runs against both databases, and one that names what the baseline's database doesn't have (a
            // table, a column, a procedure) fails there with an error (207, 208, 2812) before it returns anything. Its unknown,
            // which accepts any value, would otherwise pass as a value both may return.
            if (now.Kind == IrObjectKind.Query && Unbound(was, now) is { } unbound)
                return Unbridgeable(now.Id, unbound.Path, $"its text doesn't run against the baseline's database ({unbound.Detail.TrimEnd('.')}): ship the change to the database first, or the query in a later release");

            var parameters = Parameters(now.Id, was.Parameters, now.Parameters);
            var (bridged, results) = Results(now.Id, was, now);
            if (parameters is null || !bridged)
                return null;
            return now with { Parameters = [.. parameters], Results = results, ReturnsCode = was.ReturnsCode || now.ReturnsCode };
        }

        /// <summary>
        /// An unknown of the baseline's for a name its reading of the query file couldn't bind, where the candidate's reading
        /// could; null when there is none.
        /// </summary>
        private IrUnknown? Unbound(IrObject was, IrObject now)
        {
            var own = candidate.Unknowns.Where(u => u.Object == now.Id).Select(u => (u.Path, u.Reason)).ToHashSet();
            return baseline.Unknowns.FirstOrDefault(u => u.Object == was.Id
                && u.Reason is IrUnknownReason.UnresolvedTable or IrUnknownReason.UnresolvedColumn or IrUnknownReason.MissingProcedure
                && !own.Contains((u.Path, u.Reason)));
        }

        /// <summary>A type as its SQL type spells it where it has one (<c>decimal(9,3)</c>), as the IR says it otherwise.</summary>
        private static string Spelt(IrType type) => type is IrScalar { Sql: { } sql } ? sql : IrClaimText.Type(type);

        /// <summary>A change that can't be bridged, reported; null, for the object it leaves out.</summary>
        private IrObject? Unbridgeable(string id, string path, string what)
        {
            findings.Add(new IrCombinationFinding(IrCombinationKind.Unbridgeable, id, path, what));
            return null;
        }

        /// <summary>
        /// The parameters the combined function sends: those both procedures have, each of the narrower type and with a default
        /// only where both have one; null when one can't be bridged, which is reported. One only one procedure has is left out
        /// when it has a default there, since the other procedure refuses it (error 8144) and this one fills it in; without a
        /// default the procedure that has it refuses a call that leaves it out (error 201).
        /// </summary>
        private List<IrParameter>? Parameters(string id, EquatableArray<IrParameter> was, EquatableArray<IrParameter> now)
        {
            var before = was.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var after = now.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var combined = new List<IrParameter>();
            var bridged = true;
            foreach (var parameter in now)
            {
                var path = IrPath.Render([new IrPathKey("parameters"), new IrPathKey(parameter.Name)]);
                if (!before.TryGetValue(parameter.Name, out var old))
                {
                    if (!parameter.HasDefault)
                        bridged = Unbridgeable(id, path, $"gained the required parameter {parameter.Name}, which the baseline's procedure doesn't take: give it a default first, or ship the change in two releases") is not null;
                    continue;
                }

                if (old.TableType is not null || parameter.TableType is not null)
                {
                    if (TableValued(id, path, old, parameter) is { } tableValued)
                        combined.Add(tableValued with { HasDefault = old.HasDefault && parameter.HasDefault });
                    else
                        bridged = false;
                    continue;
                }

                var (type, why) = Narrower(old.Type, parameter.Type);
                if (type is null)
                {
                    bridged = Unbridgeable(id, path, $"changed type from {Spelt(old.Type)} to {Spelt(parameter.Type)}, {why}: ship the change in two releases") is not null;
                    continue;
                }

                // An OUTPUT parameter is declared once, as one type, and its value both goes and comes back: sent, the narrower
                // type is taken by both, but returned, the server converts each procedure's value to it, failing on one past its
                // range (8114) and cutting a longer string short without an error. So only one of the same type in both is bridged.
                if (old.IsOutput && parameter.IsOutput && old.Type != parameter.Type)
                {
                    bridged = Unbridgeable(id, path, $"changed the type of the OUTPUT parameter {parameter.Name} from {Spelt(old.Type)} to {Spelt(parameter.Type)}: it is declared as one type, which can't both send only what both take and hold what both give back, so ship the change in two releases") is not null;
                    continue;
                }

                // Sent as OUTPUT, a parameter not declared so fails the call (error 8162); sent plain, an OUTPUT one takes the value.
                combined.Add(parameter with { Type = type, HasDefault = old.HasDefault && parameter.HasDefault, IsOutput = old.IsOutput && parameter.IsOutput });
            }

            foreach (var gone in was.Where(p => !after.Contains(p.Name) && !p.HasDefault))
            {
                var path = IrPath.Render([new IrPathKey("parameters"), new IrPathKey(gone.Name)]);
                bridged = Unbridgeable(id, path, $"lost the parameter {gone.Name}, which the baseline's procedure requires: give it a default first, or ship the change in two releases") is not null;
            }

            return bridged ? combined : null;
        }

        /// <summary>
        /// The result sets, matched by position as the functions read them, each widened; a trailing set only one database returns
        /// may be missing against the other. Not bridged, and reported, when a set is read another way (rows against JSON) or when
        /// the sets can't all be named; null results when either can't be listed.
        /// </summary>
        private (bool Bridged, EquatableArray<IrResult>? Results) Results(string id, IrObject was, IrObject now)
        {
            // Results that can't be listed are handed over as they arrive, whatever they are: so are both's.
            if (was.Results is not { } before || now.Results is not { } after)
                return (true, null);

            for (var i = 0; i < Math.Min(before.Count, after.Count); i++)
            {
                if (before[i].Encoding != after[i].Encoding)
                {
                    Unbridgeable(id, IrPath.Render([new IrPathKey("results"), new IrPathIndex(i)]),
                        $"changed from {Encoding(before[i].Encoding)} to {Encoding(after[i].Encoding)}, which one function can't read both ways: ship the change in two releases");
                    return (false, null);
                }
            }

            // Sets are named all or none (decision 0070), by the config file, so a set only the baseline returns needs a name of
            // its own there, which none of the candidate's has. Without one it can't be read under a name; the function then
            // sets it aside, as it does any set after those it knows (decision 0063), but under "objects": "strict" it refuses it.
            var named = now.SetNames is not null;
            if (named && before.Count > after.Count && (was.SetNames is not { } old || old.Skip(after.Count).Intersect(now.SetNames ?? []).Any()))
            {
                if (options.StrictObjects)
                {
                    Unbridgeable(id, "results", "returns fewer result sets than the baseline, whose others have no names of their own beside the ones the config file gives now, and the function, with \"objects\": \"strict\", refuses a set it doesn't know: ship the change in two releases");
                    return (false, null);
                }

                before = [.. before.Take(after.Count)];
            }

            var at = now.At;
            var results = new List<IrResult>();
            for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
            {
                IrPathSegment[] path = [new IrPathKey("results"), new IrPathIndex(i)];
                var result = (i < before.Count, i < after.Count) switch
                {
                    (true, true) => Result(id, path, before[i], after[i]),
                    (false, _) => Missing(after[i], at),
                    (_, false) => Missing(before[i] with { Type = Adopt(before[i].Type) }, at),
                };
                results.Add(named ? result with { Name = result.Name ?? before[i].Name } : result with { Name = null });
            }

            return (true, [.. results]);

            static string Encoding(IrEncoding encoding) => encoding == IrEncoding.Rowset ? "rows" : "JSON";

            // A set the other database doesn't return is missing against it, and its function doesn't count on it.
            static IrResult Missing(IrResult result, string at) =>
                result with { AbsentBecause = [.. result.AbsentBecause, new IrNullReason(IrNullRule.NotInBoth, at)] };
        }

        /// <summary>A result set both return, widened: its type, its reasons to be missing, and how many rows it has.</summary>
        private IrResult Result(string id, IrPathSegment[] path, IrResult was, IrResult now)
        {
            return now with
            {
                Type = Join(id, path, was.Type, now.Type),
                AbsentBecause = Reasons(was.AbsentBecause, now.AbsentBecause),
                Cardinality = was.Cardinality == now.Cardinality ? now.Cardinality
                    : was.Cardinality == IrCardinality.Many || now.Cardinality == IrCardinality.Many ? IrCardinality.Many
                    : IrCardinality.AtMostOne,
            };
        }

        /// <summary>
        /// The unknowns of the objects kept: the candidate's, and the baseline's in results at a place the candidate has none,
        /// since a value unknown in either is unknown in the combined result. A parameter unknown in one only can't be bridged, and
        /// results that can't be listed in either are the candidate's or the baseline's whole unknown, which the candidate's
        /// then says. An unknown only the baseline has places it in the baseline's source.
        /// </summary>
        private EquatableArray<IrUnknown> Unknowns(List<IrObject> objects)
        {
            var kept = objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
            var own = candidate.Unknowns.Where(u => kept.ContainsKey(u.Object)).ToList();
            var at = own.Select(u => (u.Object, u.Path)).ToHashSet();
            var theirs = baseline.Unknowns.Where(u => kept.TryGetValue(u.Object, out var obj) && obj.Results is not null
                && u.Path.StartsWith("results", StringComparison.Ordinal) && !at.Contains((u.Object, u.Path)));
            return [.. own, .. theirs];
        }
    }
}
