namespace Haruspex.Ir;

public static partial class IrFit
{
    private sealed partial class ObjectWalk
    {
        /// <summary>The pairs of type names followed so far, accepting first, which ends the walk at a recursive type.</summary>
        private readonly HashSet<(string Accepting, string Given)> followed = [];

        /// <summary>
        /// The walk over two types at one place: whether every value of the one given (<c>given</c>) is one the other
        /// (<c>accepting</c>) accepts. Which document each is from depends on the flow: for a result the application's type
        /// accepts, for a parameter the procedure's does. A finding always has the application's claim before and the database's
        /// after, whichever accepts.
        /// </summary>
        /// <remarks>
        /// A union is tried variant by variant: a value fits it when it fits one variant whole. A trial walks into findings of its
        /// own, with a copy of the pairs followed, so a variant that doesn't fit leaves nothing behind: had a pair it followed
        /// been kept, a later walk would take that pair as fitting.
        /// </remarks>
        private sealed partial class Values(ObjectWalk walk, Flow flow, List<IrFitFinding> findings, HashSet<(string Accepting, string Given)>? trial = null)
        {
            private readonly HashSet<(string Accepting, string Given)> followed = trial ?? walk.followed;

            private IrDocument Accepting => flow == Flow.Results ? walk.Application : walk.Database;

            private IrDocument Given => flow == Flow.Results ? walk.Database : walk.Application;

            /// <summary>Why a value that doesn't fit is refused, in the application's terms.</summary>
            private string Refusal => flow == Flow.Results
                ? "the application's schema refuses some of what the procedure may now return"
                : "the application may send what the procedure no longer takes";

            public void Type(IrPathSegment[] path, IrType accepting, IrType given)
            {
                switch (accepting, given)
                {
                    // In a result, an unknown is written as a schema that accepts anything (z.unknown(), {}). For a parameter it is
                    // the procedure's type that accepts, and nothing is known of what that takes.
                    case (IrUnknownType, _) when flow == Flow.Results:
                        return;
                    case (IrUnknownType, _):
                        Refuse(IrChangeKind.TypeChanged, path, "the procedure's type of it isn't known, so it can't be shown to take what the application may send", IrClaimText.Type(accepting), IrClaimText.Type(given));
                        return;
                    case (_, IrUnknownType unknown):
                        if (flow == Flow.Results)
                            Refuse(IrChangeKind.BecameUnknown, path, "the application's schema can't be shown to accept it", IrClaimText.Type(accepting), IrWireNames.Of(unknown.Reason));
                        else
                            Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                        return;
                    case (IrUnion union, IrUnion variants):
                        foreach (var variant in variants.Variants)
                            OneOf(path, union, variant);
                        return;
                    case (IrUnion union, _):
                        OneOf(path, union, given);
                        return;
                    case (_, IrUnion variants):
                        foreach (var variant in variants.Variants)
                            Type(path, accepting, variant);
                        return;
                }

                // Any JSON (0.28) is held only where JSON is, so it accepts whatever is given there; narrowed to an object or an array,
                // it accepts only what is one.
                if (accepting is IrAnyJson any)
                {
                    if (any.ObjectOrArray && given is not (IrArray or IrTuple or IrRecord or IrRef or IrAnyJson { ObjectOrArray: true }))
                        Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                    return;
                }

                if (accepting.GetType() != given.GetType())
                {
                    Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                    return;
                }

                switch (accepting)
                {
                    case IrScalar scalar:
                        Scalar(path, scalar, (IrScalar)given);
                        break;
                    case IrArray array:
                        Array(path, array, (IrArray)given);
                        break;
                    case IrTuple tuple:
                        Tuple(path, tuple, (IrTuple)given);
                        break;
                    case IrRecord record:
                        var values = (IrRecord)given;
                        IrPathSegment[] inside = [.. path, new IrPathValues()];
                        Nullability(inside, record.ValuesNullableBecause, values.ValuesNullableBecause);
                        Type(inside, record.Values, values.Values);
                        break;
                    case IrRef reference:
                        Reference(path, reference.Name, ((IrRef)given).Name);
                        break;
                    case IrJsonText text:
                        JsonText(path, text, (IrJsonText)given);
                        break;
                    case IrImport:
                        // The application's own type, named in its config file: only the same one is known to accept the same values.
                        if (accepting != given)
                            Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                        break;
                    case IrUnion or IrUnknownType or IrAnyJson:
                        // Each is decided above, before the kinds are compared.
                        break;
                }
            }

            /// <summary>A value that must fit one of a union's variants whole: each is tried, and the union is refused whole when none fits.</summary>
            private void OneOf(IrPathSegment[] path, IrUnion union, IrType given)
            {
                foreach (var variant in union.Variants)
                {
                    var trial = new List<IrFitFinding>();
                    new Values(walk, flow, trial, [.. followed]).Type(path, variant, given);
                    if (trial.All(f => f.Verdict == IrFitVerdict.Ignored))
                    {
                        findings.AddRange(trial);
                        return;
                    }
                }

                Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(union), IrClaimText.Type(given));
            }

            private void Scalar(IrPathSegment[] path, IrScalar accepting, IrScalar given)
            {
                if (accepting.Kind != given.Kind)
                {
                    Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                    return;
                }

                var was = accepting.Facets ?? new IrFacets();
                var now = given.Facets ?? new IrFacets();
                if (accepting.Format != given.Format)
                {
                    // Integers of another size are numbers alike, checked by the range each format allows; any other format is
                    // another form of value (a bigint in a row arrives as a string, a decimal as a number of another precision).
                    if (Range(accepting) is not { } wide || Range(given) is not { } narrow || !Within(Lower(was.Min, wide.Min), Upper(was.Max, wide.Max), Lower(now.Min, narrow.Min), Upper(now.Max, narrow.Max), now.Values))
                    {
                        Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                        return;
                    }
                }
                else
                {
                    if (was.Min is { } least && !AtLeast(least, now.Min, now.Values))
                        Refuse(IrChangeKind.MinChanged, path, Refusal, IrClaimText.Min(was.Min), IrClaimText.Min(now.Min));
                    if (was.Max is { } most && !AtMost(most, now.Max, now.Values))
                        Refuse(IrChangeKind.MaxChanged, path, Refusal, IrClaimText.Max(was.Max), IrClaimText.Max(now.Max));
                }

                // An xml parameter typed by a schema collection, which the IR names but doesn't hold: one that narrows an element fails
                // the old call under the same name (observed: 6926). Untyped xml takes any well-formed XML, whatever was sent before.
                if (flow == Flow.Parameters && accepting.Kind == IrScalarKind.Xml && !string.Equals(accepting.Sql, "xml", StringComparison.OrdinalIgnoreCase))
                {
                    Refuse(IrChangeKind.TypeUnproven, path, "the IR doesn't hold its definition (an xml schema collection), which may have changed under its name", accepting.Sql, given.Sql);
                    return;
                }

                if (flow == Flow.Parameters && accepting.Kind is IrScalarKind.Date or IrScalarKind.Time or IrScalarKind.Datetime && accepting.Sql != given.Sql
                    && !IrDateTimes.Takes(accepting.Sql, given.Sql))
                {
                    Refuse(IrChangeKind.SqlTypeChanged, path, "the procedure's type holds a narrower range or a coarser fraction, so a value the application may send is refused or rounded without an error", accepting.Sql, given.Sql);
                }

                Length(path, was, now);
                if (was.Digits is { } digits && !(now.Digits is { } fewer && fewer.Scale <= digits.Scale && fewer.Precision - fewer.Scale <= digits.Precision - digits.Scale))
                    Refuse(IrChangeKind.DigitsChanged, path, Refusal, IrClaimText.Digits(was.Digits), IrClaimText.Digits(now.Digits));
                ValueSet(path, was.Values, now.Values);
            }

            /// <summary>
            /// The lengths: what is given may be no longer than what accepts, in the same unit, and exactly as long when that is
            /// fixed; and no shorter than its minimum, in UTF-16 code units, of which a fixed length given in bytes may hold fewer
            /// (<see cref="IrLength.FewestUtf16Units"/>). A length counted in another unit can't be compared, so it doesn't fit.
            /// </summary>
            private void Length(IrPathSegment[] path, IrFacets was, IrFacets now)
            {
                if (was.Length is { } length && !(now.Length is { } given
                    && (given.Unit, given.Encoding) == (length.Unit, length.Encoding)
                    && (length.IsFixed ? given.IsFixed && given.Value == length.Value : given.Value <= length.Value)))
                {
                    Refuse(IrChangeKind.LengthChanged, path, Refusal, IrClaimText.Length(was.Length), IrClaimText.Length(now.Length));
                }

                // What the value now guarantees, its minimum or a fixed length's fewest code units, must imply the minimum accepted.
                if (was.MinLength is { } minimum && now.MinLength?.Implies(minimum) != true
                    && !(now.Length is { IsFixed: true } filled && new IrMinLength(filled.FewestUtf16Units, filled.From).Implies(minimum)))
                {
                    Refuse(IrChangeKind.MinLengthChanged, path, Refusal, IrClaimText.MinLength(was.MinLength), IrClaimText.MinLength(now.MinLength));
                }
            }

            /// <summary>
            /// The value sets: every value given must be one of the accepting set's, matched as that set matches. A value of an exact
            /// set is matched by any set that lists it; a set matched otherwise fits only one matched the same way, by the same fold.
            /// </summary>
            private void ValueSet(IrPathSegment[] path, IrValueSet? was, IrValueSet? now)
            {
                if (was is null)
                    return;
                if (now is null)
                {
                    Refuse(IrChangeKind.ValueSetRemoved, path, Refusal, IrClaimText.Items(was.Items), null);
                    return;
                }

                var added = now.Items.Except(was.Items).ToList();
                if (added.Count > 0)
                {
                    Refuse(IrChangeKind.ValuesAdded, path, Refusal, null, IrClaimText.Items(added));
                    return;
                }

                var sameFold = was.Fold == now.Fold && (was.Fold is not { } fold || Accepting.Folds.GetValueOrDefault(fold) == Given.Folds.GetValueOrDefault(fold));
                if (now.Match != IrValueMatch.Exact && (now.Match != was.Match || !sameFold))
                    Refuse(IrChangeKind.ValueMatchChanged, path, Refusal, Match(was), Match(now));

                static string Match(IrValueSet set) => IrWireNames.Of(set.Match) + (set.Fold is { } fold ? $" ({fold})" : "");
            }

            private void Array(IrPathSegment[] path, IrArray accepting, IrArray given)
            {
                if ((given.MinItems ?? 0) < (accepting.MinItems ?? 0) || (accepting.MaxItems is { } most && !(given.MaxItems <= most)))
                    Refuse(IrChangeKind.ItemsBoundsChanged, path, Refusal, IrClaimText.Items(accepting.MinItems, accepting.MaxItems), IrClaimText.Items(given.MinItems, given.MaxItems));

                IrPathSegment[] items = [.. path, new IrPathItems()];
                Nullability(items, accepting.ItemsNullableBecause, given.ItemsNullableBecause);
                Type(items, accepting.Items, given.Items);
            }

            private void Tuple(IrPathSegment[] path, IrTuple accepting, IrTuple given)
            {
                if (accepting.Items.Count != given.Items.Count)
                {
                    Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
                    return;
                }

                for (var i = 0; i < accepting.Items.Count; i++)
                {
                    IrPathSegment[] element = [.. path, new IrPathIndex(i)];
                    Nullability(element, accepting.Items[i].NullableBecause, given.Items[i].NullableBecause);
                    Type(element, accepting.Items[i].Type, given.Items[i].Type);
                }
            }

            private void JsonText(IrPathSegment[] path, IrJsonText accepting, IrJsonText given)
            {
                // JSON text whose content isn't typed is checked as a string, which any JSON text is.
                if (accepting.Content is not { } content)
                    return;
                if (given.Content is { } inside)
                    Type([.. path, new IrPathJson()], content, inside);
                else
                    Refuse(IrChangeKind.TypeChanged, path, Refusal, IrClaimText.Type(accepting), IrClaimText.Type(given));
            }

            /// <summary>
            /// An object type on each side, compared field by field, once for each pair of names. A key the accepting type doesn't
            /// list is set aside, or refused under <c>"objects": "strict"</c>; a key it lists must arrive whenever it requires it.
            /// A key's order is no matter: an object is read by its keys' names.
            /// </summary>
            private void Reference(IrPathSegment[] path, string accepting, string given)
            {
                if (!followed.Add((accepting, given)))
                    return;

                var fields = Given.Types[given].Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
                var known = Accepting.Types[accepting].Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
                foreach (var field in Accepting.Types[accepting].Fields)
                {
                    IrPathSegment[] at = [.. path, new IrPathKey(field.Name)];
                    if (!fields.TryGetValue(field.Name, out var arrives))
                    {
                        // A key the schema already allows to be left out may go: it then never arrives, which it accepts.
                        if (field.Presence == IrPresence.Always)
                            Refuse(IrChangeKind.FieldRemoved, at, "the application's schema requires the key", IrClaimText.Value(field.Type, field.Nullable), null);
                        continue;
                    }

                    Nullability(at, field.NullableBecause, arrives.NullableBecause);
                    if (field.Presence == IrPresence.Always && arrives.Presence != IrPresence.Always)
                        Refuse(IrChangeKind.PresenceChanged, at, "the application's schema requires the key", IrWireNames.Of(field.Presence), IrWireNames.Of(arrives.Presence));

                    // A key left out when null is written as optional and never null: it may now come as null.
                    if (field.Presence == IrPresence.OmittedWhenNull && arrives.Presence != IrPresence.OmittedWhenNull && arrives.NullableBecause.Count > 0)
                        Refuse(IrChangeKind.PresenceChanged, at, "the application's schema has the key left out when null, and refuses a null", IrWireNames.Of(field.Presence), IrWireNames.Of(arrives.Presence));
                    Type(at, field.Type, arrives.Type);
                }

                foreach (var field in Given.Types[given].Fields.Where(f => !known.ContainsKey(f.Name)))
                {
                    var strict = walk.Options.StrictObjects;
                    Report(
                        IrChangeKind.FieldAdded,
                        [.. path, new IrPathKey(field.Name)],
                        strict ? IrFitVerdict.Refused : IrFitVerdict.Ignored,
                        strict ? "the application's schema, with \"objects\": \"strict\", refuses a key it doesn't list" : "the application's schema strips a key it doesn't list",
                        null,
                        IrClaimText.Value(field.Type, field.Nullable));
                }
            }

            /// <summary>A value given that may be null, where the accepting one never is.</summary>
            private void Nullability(IrPathSegment[] path, Collections.EquatableArray<IrNullReason> accepting, Collections.EquatableArray<IrNullReason> given)
            {
                if (accepting.Count == 0 && given.Count > 0)
                    Refuse(IrChangeKind.BecameNullable, path, "the application's schema refuses null", null, null);
            }

            private void Refuse(IrChangeKind kind, IrPathSegment[] path, string why, string? accepting, string? given) =>
                Report(kind, path, IrFitVerdict.Refused, why, accepting, given);

            /// <summary>A finding, with the application's claim before and the database's after, whichever of the two accepts.</summary>
            private void Report(IrChangeKind kind, IrPathSegment[] path, IrFitVerdict verdict, string why, string? accepting, string? given)
            {
                var (before, after) = flow == Flow.Results ? (accepting, given) : (given, accepting);
                walk.Add(findings, kind, path, verdict, why, before, after);
            }

            /// <summary>
            /// The range of values an integer format allows, where formats of other sizes are compared by range: in a result, the
            /// formats a row and JSON both deliver as a number; for a parameter, every integer, since the server converts what is
            /// sent to the type it declares. Null for any other format.
            /// </summary>
            private (string Min, string Max)? Range(IrScalar scalar) => scalar.Format switch
            {
                IrFormat.Uint8 => ("0", "255"),
                IrFormat.Int16 => ("-32768", "32767"),
                IrFormat.Int32 => ("-2147483648", "2147483647"),
                IrFormat.Int64 when flow == Flow.Parameters => ("-9223372036854775808", "9223372036854775807"),
                _ => null,
            };

            private static IrBound Lower(IrBound? own, string range) => own ?? new IrBound(range, true, new IrFactSource(IrFactSourceKind.Type, ""));

            private static IrBound Upper(IrBound? own, string range) => own ?? new IrBound(range, true, new IrFactSource(IrFactSourceKind.Type, ""));

            /// <summary>Whether a range given lies within one that accepts.</summary>
            private static bool Within(IrBound min, IrBound max, IrBound givenMin, IrBound givenMax, IrValueSet? values) =>
                AtLeast(min, givenMin, values) && AtMost(max, givenMax, values);

            /// <summary>
            /// Whether every value given is at least <paramref name="least"/>: its own lower bound is as high, or every value of its
            /// exact set is (a set of a type's values keeps the type's own range as its bounds, which may be wider).
            /// </summary>
            private static bool AtLeast(IrBound least, IrBound? given, IrValueSet? values) =>
                (given is { } bound && IrNumbers.Compare(bound.Value, least.Value) is var order && (order > 0 || (order == 0 && (least.Inclusive || !bound.Inclusive))))
                || (Numbers(values) is { } items && items.All(v => IrNumbers.Compare(v, least.Value) is var each && (each > 0 || (each == 0 && least.Inclusive))));

            /// <summary>Whether every value given is at most <paramref name="most"/>, as <see cref="AtLeast"/> is for a lower bound.</summary>
            private static bool AtMost(IrBound most, IrBound? given, IrValueSet? values) =>
                (given is { } bound && IrNumbers.Compare(bound.Value, most.Value) is var order && (order < 0 || (order == 0 && (most.Inclusive || !bound.Inclusive))))
                || (Numbers(values) is { } items && items.All(v => IrNumbers.Compare(v, most.Value) is var each && (each < 0 || (each == 0 && most.Inclusive))));

            /// <summary>The numbers of an exact set of numbers, or null.</summary>
            private static List<string>? Numbers(IrValueSet? values) =>
                values is { Match: IrValueMatch.Exact } set && set.Items.All(i => !i.IsString) ? [.. set.Items.Select(i => i.Text)] : null;
        }
    }
}
