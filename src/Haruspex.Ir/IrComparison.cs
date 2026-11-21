using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// What changed between two documents, claim by claim: the structural comparison <c>haruspex check</c> reports, so that a
/// developer reads "<c>shop.list_orders results[0][].note: became nullable</c>" rather than a diff of two JSON files.
/// </summary>
/// <remarks>
/// <para>
/// Only claims are compared: what a reader of the document, or of code generated from it, may rely on. What says where a
/// claim came from is not a claim, so it is not compared: every location (<c>at</c>), the reasons a value may be null or
/// absent (only whether there is one is a claim), the sources of facts (<c>from</c>), the package a source was read from and
/// its hash (but a declared column's schema files, whose text is the claim: 0.28), an unknown's reason and detail (only that the value is unknown is a claim), the overrides (their effect is in
/// the types), and a type's identity and whether it is recursive (both follow from the types). So editing a procedure without
/// changing what it returns, or rebuilding a DACPAC, changes nothing here. Why: docs/decisions/0050-check-compares-claims-not-files.md.
/// </para>
/// <para>
/// Objects are matched by id, parameters and fields by name, result sets by position. A reference is followed into the type it
/// names on each side, so a type that is renamed is still compared field by field; each pair of names is followed once per
/// object, so a recursive type ends the walk. A change of a scalar's kind, or of a node's kind, is reported alone: what is
/// inside it is no longer comparable. The changes are in a stable order: the document's own, then each object's by id, each
/// in the order its walk meets them.
/// </para>
/// </remarks>
public static class IrComparison
{
    /// <summary>Every change in what <paramref name="after"/> claims from what <paramref name="before"/> claims; empty when they claim the same.</summary>
    public static EquatableArray<IrChange> Compare(IrDocument before, IrDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<IrChange>();
        Source(before, after, changes);
        Folds(before, after, changes);
        Columns(before, after, changes);

        var earlier = before.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var later = after.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        foreach (var id in earlier.Keys.Union(later.Keys).Order(StringComparer.Ordinal))
        {
            switch (earlier.GetValueOrDefault(id), later.GetValueOrDefault(id))
            {
                case (not null, null):
                    changes.Add(new IrChange(IrChangeKind.ObjectRemoved, id, ""));
                    break;
                case (null, not null):
                    changes.Add(new IrChange(IrChangeKind.ObjectAdded, id, ""));
                    break;
                case ({ } was, { } now):
                    new ObjectWalk(before, after, id, changes).Object(was, now);
                    break;
            }
        }

        return [.. changes];
    }

    private static void Source(IrDocument before, IrDocument after, List<IrChange> changes)
    {
        if (before.Source.Dialect != after.Source.Dialect)
            changes.Add(new IrChange(IrChangeKind.DialectChanged, "", "source.dialect", before.Source.Dialect, after.Source.Dialect));
        if (before.Source.Session != after.Source.Session)
            changes.Add(new IrChange(IrChangeKind.SessionChanged, "", "source.session", IrClaimText.Session(before.Source.Session), IrClaimText.Session(after.Source.Session)));
    }

    private static void Folds(IrDocument before, IrDocument after, List<IrChange> changes)
    {
        foreach (var name in before.Folds.Keys.Union(after.Folds.Keys).Order(StringComparer.Ordinal))
        {
            var was = before.Folds.GetValueOrDefault(name);
            var now = after.Folds.GetValueOrDefault(name);
            if (was != now)
                changes.Add(new IrChange(IrChangeKind.FoldChanged, "", "folds" + IrPath.Key(name), IrClaimText.Fold(was), IrClaimText.Fold(now)));
        }
    }

    /// <summary>
    /// The columns declared to hold JSON of a schema (0.28). A declaration is a claim made in a schema file, so a file whose text
    /// changed is a change though no type the document holds changed: the next value read from the column is held to the new text.
    /// </summary>
    private static void Columns(IrDocument before, IrDocument after, List<IrChange> changes)
    {
        var earlier = before.Columns.ToDictionary(c => c.Column, StringComparer.Ordinal);
        var later = after.Columns.ToDictionary(c => c.Column, StringComparer.Ordinal);
        foreach (var column in earlier.Keys.Union(later.Keys).Order(StringComparer.Ordinal))
        {
            var was = earlier.GetValueOrDefault(column);
            var now = later.GetValueOrDefault(column);
            if (was != now)
                changes.Add(new IrChange(IrChangeKind.ColumnDeclarationChanged, "", "columns" + IrPath.Key(column), IrClaimText.Declaration(was), IrClaimText.Declaration(now)));
        }
    }

    /// <summary>
    /// The walk over one object present in both documents. It holds the changes it finds and the pairs of type names it has
    /// followed, which is what ends the walk at a recursive type; both live only as long as the walk.
    /// </summary>
    private sealed class ObjectWalk(IrDocument before, IrDocument after, string id, List<IrChange> changes)
    {
        private IrDocument Before => before;

        private IrDocument After => after;

        /// <summary>The definition of a table-valued parameter's type in <paramref name="document"/>, or null for any other parameter.</summary>
        private static IrTableType? TableTypeOf(IrDocument document, IrParameter parameter) =>
            parameter.TableType is { } key && document.TableTypes.TryGetValue(key, out var type) ? type : null;

        private readonly HashSet<(string Before, string After)> followed = [];

        public void Object(IrObject was, IrObject now)
        {
            if (was.Name != now.Name)
                Add(IrChangeKind.ObjectRenamed, [], was.Name, now.Name);
            if (was.Kind != now.Kind)
                Add(IrChangeKind.ObjectKindChanged, [], IrWireNames.Of(was.Kind), IrWireNames.Of(now.Kind));

            // What a query sends is part of its contract: an edited file the committed module doesn't run is a change.
            if (was.Text != now.Text)
                Add(IrChangeKind.TextChanged, []);

            // Whether a code comes back decides the shape of the object's function.
            if (was.ReturnsCode != now.ReturnsCode)
                Add(IrChangeKind.ReturnsCodeChanged, [], Flag(was.ReturnsCode), Flag(now.ReturnsCode));
            Parameters(was.Parameters, now.Parameters);
            Results(was.Results, now.Results);
        }

        private void Parameters(EquatableArray<IrParameter> was, EquatableArray<IrParameter> now)
        {
            IrPathSegment[] parameters = [new IrPathKey("parameters")];
            Named(parameters, was, now, p => p.Name, IrChangeKind.ParameterRemoved, IrChangeKind.ParameterAdded, IrChangeKind.ParametersReordered, p => IrClaimText.Value(p.Type, nullable: false), Parameter);

            void Parameter(IrPathSegment[] path, IrParameter before, IrParameter after)
            {
                Type(path, before.Type, after.Type);
                if (TableTypeOf(Before, before) is var was && TableTypeOf(After, after) is var now && !(was is null ? now is null : now is not null && was.Defines(now)))
                    Add(IrChangeKind.TableTypeChanged, path, was is null ? null : IrClaimText.TableType(was), now is null ? null : IrClaimText.TableType(now));
                if (before.HasDefault != after.HasDefault)
                    Add(IrChangeKind.ParameterDefaultChanged, path, Flag(before.HasDefault), Flag(after.HasDefault));
                if (before.IsOutput != after.IsOutput)
                    Add(IrChangeKind.ParameterDirectionChanged, path, Flag(before.IsOutput), Flag(after.IsOutput));
            }
        }

        private void Results(EquatableArray<IrResult>? was, EquatableArray<IrResult>? now)
        {
            IrPathSegment[] results = [new IrPathKey("results")];
            switch (was, now)
            {
                case (null, null):
                    return;
                case (not null, null):
                    Add(IrChangeKind.ResultsBecameUnknown, results);
                    return;
                case (null, { } known):
                    Add(IrChangeKind.ResultsBecameKnown, results, after: IrClaimText.ResultSets(known.Count));
                    return;
                case ({ } before, { } after):
                    for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
                    {
                        IrPathSegment[] path = [.. results, new IrPathIndex(i)];
                        if (i >= after.Count)
                            Add(IrChangeKind.ResultRemoved, path);
                        else if (i >= before.Count)
                            Add(IrChangeKind.ResultAdded, path);
                        else
                            Result(path, before[i], after[i]);
                    }

                    return;
            }
        }

        private void Result(IrPathSegment[] path, IrResult was, IrResult now)
        {
            // A set's name is the key its function returns it under, so a renamed set breaks its callers.
            if (was.Name != now.Name)
                Add(IrChangeKind.ResultRenamed, path, was.Name, now.Name);
            if (was.Encoding != now.Encoding)
                Add(IrChangeKind.EncodingChanged, path, IrWireNames.Of(was.Encoding), IrWireNames.Of(now.Encoding));
            if (was.Cardinality != now.Cardinality)
                Add(IrChangeKind.CardinalityChanged, path, IrWireNames.Of(was.Cardinality), IrWireNames.Of(now.Cardinality));
            if (was.MayBeAbsent != now.MayBeAbsent)
                Add(now.MayBeAbsent ? IrChangeKind.BecameMayBeAbsent : IrChangeKind.BecameAlwaysPresent, path);
            Type(path, was.Type, now.Type);
        }

        /// <summary>
        /// Two type nodes at one place. A node of another kind is one change, said in the terms of an unknown when either side
        /// is one; otherwise the node's own claims, then what is inside it.
        /// </summary>
        private void Type(IrPathSegment[] path, IrType was, IrType now)
        {
            if (was.GetType() != now.GetType())
            {
                switch (was, now)
                {
                    case (_, IrUnknownType unknown):
                        Add(IrChangeKind.BecameUnknown, path, IrClaimText.Type(was), IrWireNames.Of(unknown.Reason));
                        break;
                    case (IrUnknownType, _):
                        Add(IrChangeKind.BecameKnown, path, after: IrClaimText.Type(now));
                        break;
                    default:
                        Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                        break;
                }

                return;
            }

            switch (was)
            {
                case IrScalar scalar:
                    Scalar(path, scalar, (IrScalar)now);
                    break;
                case IrArray array:
                    Array(path, array, (IrArray)now);
                    break;
                case IrTuple tuple:
                    Tuple(path, tuple, (IrTuple)now);
                    break;
                case IrRecord record:
                    var values = (IrRecord)now;
                    IrPathSegment[] inside = [.. path, new IrPathValues()];
                    Nullability(inside, record.ValuesNullableBecause, values.ValuesNullableBecause);
                    Type(inside, record.Values, values.Values);
                    break;
                case IrUnion union:
                    // Variants have no names to match by. As many on each side are compared position by position, at the union's
                    // own path, which its variants share (as their unknowns do); a variant more or fewer is the union changed whole.
                    var variants = ((IrUnion)now).Variants;
                    if (union.Variants.Count != variants.Count)
                    {
                        Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                        break;
                    }

                    foreach (var (one, other) in union.Variants.Zip(variants))
                        Type(path, one, other);
                    break;
                case IrRef reference:
                    Reference(path, reference.Name, ((IrRef)now).Name);
                    break;
                case IrJsonText text:
                    JsonText(path, text, (IrJsonText)now);
                    break;
                case IrImport or IrAnyJson:
                    if (was != now)
                        Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                    break;
                case IrUnknownType:
                    // Unknown on both sides: why it is unknown is a reason, not a claim.
                    break;
            }
        }

        private void Scalar(IrPathSegment[] path, IrScalar was, IrScalar now)
        {
            if (was.Kind != now.Kind)
            {
                // Facets of a string and of a number aren't comparable: the change of kind says it all.
                Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                return;
            }

            if (was.Format != now.Format)
                Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
            if (was.Sql != now.Sql)
                Add(IrChangeKind.SqlTypeChanged, path, was.Sql ?? "none", now.Sql ?? "none");
            Facets(path, was.Facets ?? new IrFacets(), now.Facets ?? new IrFacets());
        }

        /// <summary>Each fact compared without its source, which says where the fact came from and is not part of it.</summary>
        private void Facets(IrPathSegment[] path, IrFacets was, IrFacets now)
        {
            if ((was.Length?.Value, was.Length?.IsFixed, was.Length?.Unit, was.Length?.Encoding) != (now.Length?.Value, now.Length?.IsFixed, now.Length?.Unit, now.Length?.Encoding))
                Add(IrChangeKind.LengthChanged, path, IrClaimText.Length(was.Length), IrClaimText.Length(now.Length));
            if (was.MinLength?.Value != now.MinLength?.Value)
                Add(IrChangeKind.MinLengthChanged, path, IrClaimText.MinLength(was.MinLength), IrClaimText.MinLength(now.MinLength));
            if ((was.Digits?.Precision, was.Digits?.Scale) != (now.Digits?.Precision, now.Digits?.Scale))
                Add(IrChangeKind.DigitsChanged, path, IrClaimText.Digits(was.Digits), IrClaimText.Digits(now.Digits));
            if ((was.Min?.Value, was.Min?.Inclusive) != (now.Min?.Value, now.Min?.Inclusive))
                Add(IrChangeKind.MinChanged, path, IrClaimText.Min(was.Min), IrClaimText.Min(now.Min));
            if ((was.Max?.Value, was.Max?.Inclusive) != (now.Max?.Value, now.Max?.Inclusive))
                Add(IrChangeKind.MaxChanged, path, IrClaimText.Max(was.Max), IrClaimText.Max(now.Max));
            Values(path, was.Values, now.Values);
        }

        private void Values(IrPathSegment[] path, IrValueSet? was, IrValueSet? now)
        {
            switch (was, now)
            {
                case (null, null):
                    return;
                case (null, { } set):
                    Add(IrChangeKind.ValueSetAdded, path, after: IrClaimText.Items(set.Items));
                    return;
                case ({ } set, null):
                    Add(IrChangeKind.ValueSetRemoved, path, IrClaimText.Items(set.Items));
                    return;
                case ({ } before, { } after):
                    if (before.Match != after.Match || before.Fold != after.Fold)
                        Add(IrChangeKind.ValueMatchChanged, path, Match(before), Match(after));
                    var removed = before.Items.Except(after.Items).ToList();
                    var added = after.Items.Except(before.Items).ToList();
                    if (added.Count > 0)
                        Add(IrChangeKind.ValuesAdded, path, after: IrClaimText.Items(added));
                    if (removed.Count > 0)
                        Add(IrChangeKind.ValuesRemoved, path, IrClaimText.Items(removed));
                    return;
            }

            static string Match(IrValueSet set) => IrWireNames.Of(set.Match) + (set.Fold is { } fold ? $" ({fold})" : "");
        }

        private void Array(IrPathSegment[] path, IrArray was, IrArray now)
        {
            IrPathSegment[] items = [.. path, new IrPathItems()];
            if ((was.MinItems, was.MaxItems) != (now.MinItems, now.MaxItems))
                Add(IrChangeKind.ItemsBoundsChanged, path, IrClaimText.Items(was.MinItems, was.MaxItems), IrClaimText.Items(now.MinItems, now.MaxItems));
            Nullability(items, was.ItemsNullableBecause, now.ItemsNullableBecause);
            Type(items, was.Items, now.Items);
        }

        private void Tuple(IrPathSegment[] path, IrTuple was, IrTuple now)
        {
            if (was.Items.Count != now.Items.Count)
            {
                Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                return;
            }

            for (var i = 0; i < was.Items.Count; i++)
            {
                IrPathSegment[] element = [.. path, new IrPathIndex(i)];
                Nullability(element, was.Items[i].NullableBecause, now.Items[i].NullableBecause);
                Type(element, was.Items[i].Type, now.Items[i].Type);
            }
        }

        private void JsonText(IrPathSegment[] path, IrJsonText was, IrJsonText now)
        {
            switch (was.Content, now.Content)
            {
                case ({ } before, { } after):
                    Type([.. path, new IrPathJson()], before, after);
                    break;
                case (null, null):
                    break;
                default:
                    Add(IrChangeKind.TypeChanged, path, IrClaimText.Type(was), IrClaimText.Type(now));
                    break;
            }
        }

        /// <summary>
        /// A reference on each side: a change of name is said, and the two types are compared field by field, once for each
        /// pair of names, so that a type that reaches itself ends the walk.
        /// </summary>
        private static string Openness(IrObjectType type) => type.Open ? "an object that may hold other keys" : "an object of its keys alone";

        private void Reference(IrPathSegment[] path, string was, string now)
        {
            if (was != now)
                Add(IrChangeKind.TypeRenamed, path, was, now);
            if (!followed.Add((was, now)))
                return;

            // Whether its values may hold keys it doesn't list decides what a strict schema of it refuses.
            if (before.Types[was].Open != after.Types[now].Open)
                Add(IrChangeKind.TypeChanged, path, Openness(before.Types[was]), Openness(after.Types[now]));
            Named(path, before.Types[was].Fields, after.Types[now].Fields, f => f.Name, IrChangeKind.FieldRemoved, IrChangeKind.FieldAdded, IrChangeKind.FieldsReordered, f => IrClaimText.Value(f.Type, f.Nullable), Field);

            void Field(IrPathSegment[] field, IrField before, IrField after)
            {
                Nullability(field, before.NullableBecause, after.NullableBecause);
                if (before.Presence != after.Presence)
                    Add(IrChangeKind.PresenceChanged, field, IrWireNames.Of(before.Presence), IrWireNames.Of(after.Presence));
                Type(field, before.Type, after.Type);
            }
        }

        /// <summary>
        /// Members matched by name (fields, parameters): those only one side has, then those both have in another order, then
        /// each both have, in the earlier order. Only the relative order of the members both have is an order change: a member
        /// replaced by another in its place is a removal and an addition, not a reordering.
        /// </summary>
        private void Named<T>(
            IrPathSegment[] path,
            EquatableArray<T> was,
            EquatableArray<T> now,
            Func<T, string> name,
            IrChangeKind removed,
            IrChangeKind added,
            IrChangeKind reordered,
            Func<T, string> text,
            Action<IrPathSegment[], T, T> compare)
        {
            var later = now.ToDictionary(name, StringComparer.Ordinal);
            var earlier = was.ToDictionary(name, StringComparer.Ordinal);
            foreach (var member in was.Where(m => !later.ContainsKey(name(m))))
                Add(removed, [.. path, new IrPathKey(name(member))], text(member));
            foreach (var member in now.Where(m => !earlier.ContainsKey(name(m))))
                Add(added, [.. path, new IrPathKey(name(member))], after: text(member));

            var kept = was.Select(name).Where(later.ContainsKey).ToList();
            if (!kept.SequenceEqual(now.Select(name).Where(earlier.ContainsKey), StringComparer.Ordinal))
                Add(reordered, path, string.Join(", ", was.Select(name)), string.Join(", ", now.Select(name)));

            foreach (var member in was.Where(m => later.ContainsKey(name(m))))
                compare([.. path, new IrPathKey(name(member))], member, later[name(member)]);
        }

        /// <summary>A value that became nullable, with the reasons it now may be; or one that became non-null.</summary>
        private void Nullability(IrPathSegment[] path, EquatableArray<IrNullReason> was, EquatableArray<IrNullReason> now)
        {
            if (was.Count == 0 && now.Count > 0)
                changes.Add(new IrChange(IrChangeKind.BecameNullable, id, IrPath.Render(path)) { NullableBecause = now });
            else if (was.Count > 0 && now.Count == 0)
                Add(IrChangeKind.BecameNonNull, path);
        }

        private void Add(IrChangeKind kind, IrPathSegment[] path, string? before = null, string? after = null) =>
            changes.Add(new IrChange(kind, id, IrPath.Render(path), before, after));

        private static string Flag(bool value) => value ? "true" : "false";
    }
}
