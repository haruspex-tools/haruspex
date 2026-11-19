using Haruspex.Collections;

namespace Haruspex.Ir;

public static partial class IrCombination
{
    private sealed partial class Combiner
    {
        /// <summary>
        /// For each object type of the combined document, by name, the baseline's types it must also accept: one met at the same
        /// place as it in some result. A name the candidate has keeps its name; the combined type accepts every value of its own
        /// and of each of these. So a type the candidate shares between places that the baseline typed apart is wider than each
        /// place needs, which is sound, and the names the config file settled are kept.
        /// </summary>
        private readonly Dictionary<string, List<string>> partners = new(StringComparer.Ordinal);

        /// <summary>
        /// The baseline's types the combined document takes as they are, by name, with nothing of the candidate's to widen: a
        /// type only a key or a set only the baseline returns refers to. The candidate has no type of the name.
        /// </summary>
        private readonly HashSet<string> adopted = new(StringComparer.Ordinal);

        /// <summary>Where each pair of a combined type and a baseline's type was first met, for a possible rename found between them.</summary>
        private readonly Dictionary<(string Combined, string Baseline), (string Object, IrPathSegment[] Path)> met = [];

        /// <summary>
        /// The widest type of both at one place in a result: a value of either is a value of it. Two object types are merged into
        /// the candidate's, by name; two of another kind become a union of both; one unknown in either is unknown.
        /// </summary>
        /// <param name="id">The object, for a possible rename found inside.</param>
        /// <param name="path">Where in it.</param>
        /// <param name="was">The baseline's type, whose references name the baseline's types.</param>
        /// <param name="now">The other type, whose references name the combined document's: the candidate's, or one merged already.</param>
        private IrType Join(string id, IrPathSegment[] path, IrType was, IrType now)
        {
            switch (was, now)
            {
                // An unknown is written as a schema that accepts anything, so it accepts every value of the other too.
                case (_, IrUnknownType):
                    return now;
                case (IrUnknownType, _):
                    return was;
                case (IrUnion, _) or (_, IrUnion):
                    return Union(id, path, was, now);
                case (IrRef before, IrRef after):
                    Pair(after.Name, before.Name, id, path);
                    return now;
                case (IrScalar before, IrScalar after) when Widest(before, after) is { } scalar:
                    return scalar;
                case (IrArray before, IrArray after):
                    return after with
                    {
                        Items = Join(id, [.. path, new IrPathItems()], before.Items, after.Items),
                        ItemsNullableBecause = Reasons(before.ItemsNullableBecause, after.ItemsNullableBecause),
                        MinItems = before.MinItems is { } fewest && after.MinItems is { } least ? Math.Min(fewest, least) : null,
                        MaxItems = before.MaxItems is { } most && after.MaxItems is { } largest ? Math.Max(most, largest) : null,
                    };
                case (IrTuple before, IrTuple after) when before.Items.Count == after.Items.Count:
                    return new IrTuple([.. before.Items.Zip(after.Items, (b, a) => new IrElement(Join(id, path, b.Type, a.Type), Reasons(b.NullableBecause, a.NullableBecause)))]);
                case (IrRecord before, IrRecord after):
                    return new IrRecord(Join(id, [.. path, new IrPathValues()], before.Values, after.Values), Reasons(before.ValuesNullableBecause, after.ValuesNullableBecause));
                case (IrJsonText before, IrJsonText after):
                    // JSON text whose content isn't typed is checked as a string, which any JSON text is.
                    return before.Content is { } old && after.Content is { } content ? new IrJsonText(Join(id, [.. path, new IrPathJson()], old, content)) : new IrJsonText();
                case (IrImport, IrImport) when was == now:
                    return now;
                default:
                    return Union(id, path, was, now);
            }
        }

        /// <summary>
        /// A union of both's variants, where a variant of the baseline's of the same kind as one of the other's is joined with it,
        /// so that two object types are still merged into one, and any other is added.
        /// </summary>
        private IrType Union(string id, IrPathSegment[] path, IrType was, IrType now)
        {
            var variants = now is IrUnion union ? union.Variants.ToList() : [now];
            foreach (var variant in was is IrUnion old ? old.Variants : [was])
            {
                var alike = variants.FindIndex(v => Alike(variant, v));
                if (alike >= 0)
                    variants[alike] = Join(id, path, variant, variants[alike]);
                else
                    variants.Add(Adopt(variant));
            }

            var distinct = variants.Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : new IrUnion([.. distinct]);
        }

        /// <summary>Whether two variants join into one rather than standing side by side in a union.</summary>
        private bool Alike(IrType was, IrType now) => (was, now) switch
        {
            (IrRef, IrRef) or (IrArray, IrArray) or (IrRecord, IrRecord) or (IrJsonText, IrJsonText) => true,
            (IrScalar before, IrScalar after) => Widest(before, after) is not null,
            (IrTuple before, IrTuple after) => before.Items.Count == after.Items.Count,
            _ => was == now,
        };

        /// <summary>Records that the combined type <paramref name="combined"/> must also accept the baseline's <paramref name="was"/>.</summary>
        private void Pair(string combined, string was, string id, IrPathSegment[] path)
        {
            if (adopted.Contains(combined) && combined == was)
                return;
            var list = partners.TryGetValue(combined, out var found) ? found : partners[combined] = [];
            if (list.Contains(was, StringComparer.Ordinal))
                return;
            list.Add(was);
            met.TryAdd((combined, was), (id, path));
        }

        /// <summary>
        /// A type of the baseline's, taken into the combined document where nothing of the candidate's meets it: each reference
        /// names a combined type, the candidate's of the same name (which then accepts the baseline's too) or the baseline's own,
        /// adopted; and a value set matched by a fold the candidate defines otherwise is dropped, claiming less.
        /// </summary>
        private IrType Adopt(IrType was) => Folded(IrTypeTree.RenameReferences(was, name =>
        {
            if (candidate.Types.ContainsKey(name))
                Pair(name, name, "", []);
            else
                adopted.Add(name);
            return name;
        }));

        /// <summary>The type with each scalar's value set dropped where its fold isn't the same in both documents.</summary>
        private IrType Folded(IrType type) => type switch
        {
            IrScalar { Facets.Values.Fold: { } fold } scalar when !SameFold(fold) => scalar with { Facets = scalar.Facets with { Values = null } },
            IrArray array => array with { Items = Folded(array.Items) },
            IrTuple tuple => new IrTuple([.. tuple.Items.Select(e => e with { Type = Folded(e.Type) })]),
            IrRecord record => record with { Values = Folded(record.Values) },
            IrUnion union => new IrUnion([.. union.Variants.Select(Folded)]),
            IrJsonText { Content: { } content } => new IrJsonText(Folded(content)),
            _ => type,
        };

        /// <summary>Whether a fold of the baseline's means what the candidate's of the same name means, or the candidate has none.</summary>
        private bool SameFold(string fold) =>
            !candidate.Folds.TryGetValue(fold, out var now) || (baseline.Folds.TryGetValue(fold, out var was) && was == now);

        /// <summary>
        /// The reasons a value may be null or a result missing, of either: the other's, and each of the baseline's for a rule the
        /// other doesn't give. A rule decides how a reader treats it (a result missing for want of rows still arrives as JSON), so
        /// none is lost; where both give one, the other's place is kept. A reason only the baseline gives places it in the
        /// baseline's source.
        /// </summary>
        private static EquatableArray<IrNullReason> Reasons(EquatableArray<IrNullReason> was, EquatableArray<IrNullReason> now) =>
            [.. now, .. was.Where(r => !now.Any(n => n.Rule == r.Rule))];

        /// <summary>
        /// The object types of the combined document: each reachable from its objects, the candidate's widened by the baseline's
        /// each must also accept, and the baseline's adopted. Widening one can pair others, so they are worked out until nothing
        /// new is paired; each is then marked recursive exactly when it can reach itself.
        /// </summary>
        private Dictionary<string, IrObjectType> Types(List<IrObject> objects)
        {
            var types = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
            var widenedBy = new Dictionary<string, int>(StringComparer.Ordinal);
            var roots = objects.SelectMany(o => o.Parameters.Select(p => p.Type).Concat(o.Results?.Select(r => r.Type) ?? [])).ToList();
            var pending = new Queue<string>(roots.SelectMany(IrTypeTree.References).Distinct(StringComparer.Ordinal));
            while (pending.Count > 0 || Stale().FirstOrDefault() is not null)
            {
                foreach (var name in Stale())
                    pending.Enqueue(name);
                var next = pending.Dequeue();

                // A table type's row is sent, so it is narrowed with its parameter (TableValued), never widened.
                if (rows.TryGetValue(next, out var row))
                {
                    types[next] = row;
                    widenedBy[next] = 0;
                    continue;
                }

                if (types.ContainsKey(next) && widenedBy[next] == Partners(next).Count)
                    continue;

                var type = Widened(next);
                types[next] = type;
                foreach (var name in type.Fields.SelectMany(f => IrTypeTree.References(f.Type)).Where(n => !types.ContainsKey(n)))
                    pending.Enqueue(name);
            }

            // Only the types the objects reach are kept: a type of the candidate's that only a procedure left out used isn't.
            var document = new IrDocument(candidate.Source, [.. objects], types, []);
            var graph = IrTypeGraph.Of(document);
            var reached = graph.ReachableFrom(roots);
            return types.Where(t => reached.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value with { Recursive = graph.IsRecursive(t.Key) }, StringComparer.Ordinal);

            List<string> Stale() => [.. types.Keys.Where(n => widenedBy[n] != Partners(n).Count)];

            IrObjectType Widened(string name)
            {
                var partnered = Partners(name);
                widenedBy[name] = partnered.Count;
                var own = candidate.Types.TryGetValue(name, out var found) ? found : Adopted(name);
                var fields = own.Fields.ToList();
                foreach (var was in partnered.ToList())
                    fields = Fields(name, was, fields);
                return own with { Fields = [.. fields] };
            }
        }

        private List<string> Partners(string name) => partners.TryGetValue(name, out var found) ? found : [];

        /// <summary>A type of the baseline's taken as it is, its fields' types adopted.</summary>
        private IrObjectType Adopted(string name)
        {
            var type = baseline.Types[name];
            return type with { Fields = [.. type.Fields.Select(f => f with { Type = Adopt(f.Type) })] };
        }

        /// <summary>
        /// The fields of a combined type, widened to accept the baseline's type <paramref name="was"/> too: a key both have is
        /// joined, and a key only one has may be left out. A key that went with another that appeared is said as a possible rename.
        /// </summary>
        private List<IrField> Fields(string name, string was, List<IrField> now)
        {
            var before = baseline.Types[was].Fields;
            var had = before.ToDictionary(f => f.Name, StringComparer.Ordinal);
            var has = now.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            var (id, path) = met.TryGetValue((name, was), out var at) ? at : ("", []);
            var fields = now.Select(field => had.TryGetValue(field.Name, out var old) ? Field(id, [.. path, new IrPathKey(field.Name)], old, field) : OneSided(field, baseline: false)).ToList();
            fields.AddRange(before.Where(f => !has.Contains(f.Name)).Select(f => OneSided(f with { Type = Adopt(f.Type) }, baseline: true)));

            var gone = before.Where(f => !has.Contains(f.Name)).Select(f => f.Name).ToList();
            var appeared = candidate.Types.TryGetValue(name, out var own) ? own.Fields.Where(f => !had.ContainsKey(f.Name)).Select(f => f.Name).ToList() : [];
            if (gone.Count > 0 && appeared.Count > 0 && id.Length > 0)
            {
                findings.Add(new IrCombinationFinding(IrCombinationKind.PossibleRename, id, IrPath.Render(path),
                    $"{EnglishList(gone)} went and {EnglishList(appeared)} appeared; if {(gone.Count == 1 && appeared.Count == 1 ? "it was" : "one was")} renamed, read both until the baseline moves on"));
            }

            return fields;

            static string EnglishList(List<string> names) => names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
        }

        /// <summary>
        /// A key both have, joined. It is always there only where both always send it; left out when null where one leaves it
        /// out when null and the other never sends null; and otherwise it may be left out, and is null where either may send null.
        /// </summary>
        private IrField Field(string id, IrPathSegment[] path, IrField was, IrField now)
        {
            var type = Join(id, path, was.Type, now.Type);
            var joined = Joined(was, now) with { From = [.. was.From.Concat(now.From).Distinct()], Sides = null };

            // Where both databases give the field exactly as combined, the sides would only repeat it, so they are left out.
            var (before, after) = (IrFieldSide.Of(was), IrFieldSide.Of(now));
            return before == after && after == IrFieldSide.Of(joined) ? joined : joined with { Sides = new IrFieldSides(before, after) };

            IrField Joined(IrField was, IrField now) => (was.Presence, now.Presence) switch
            {
                (IrPresence.Always, IrPresence.Always) or (IrPresence.OmittedWhenNull, IrPresence.OmittedWhenNull) =>
                    now with { Type = type, NullableBecause = Reasons(was.NullableBecause, now.NullableBecause) },
                (IrPresence.Always, IrPresence.OmittedWhenNull) when !was.Nullable => now with { Type = type },
                (IrPresence.OmittedWhenNull, IrPresence.Always) when !now.Nullable => now with { Type = type, Presence = IrPresence.OmittedWhenNull, NullableBecause = was.NullableBecause },
                _ => now with { Type = type, Presence = IrPresence.Optional, NullableBecause = Reasons(SentNull(was), SentNull(now)) },
            };

            // The reasons a key's value may arrive as null: none where a null leaves the key out.
            static EquatableArray<IrNullReason> SentNull(IrField field) => field.Presence == IrPresence.OmittedWhenNull ? [] : field.NullableBecause;
        }

        /// <summary>A key only one database returns, which may be left out against the other; null where that one may send null.</summary>
        /// <param name="field">The key as the one database gives it.</param>
        /// <param name="baseline">Whether that is the baseline.</param>
        private static IrField OneSided(IrField field, bool baseline) => field with
        {
            Presence = IrPresence.Optional,
            NullableBecause = field.Presence == IrPresence.OmittedWhenNull ? [] : field.NullableBecause,
            Sides = baseline ? new IrFieldSides(IrFieldSide.Of(field), null) : new IrFieldSides(null, IrFieldSide.Of(field)),
        };

        /// <summary>The folds the combined document's value sets name, each as the candidate defines it, or the baseline where it doesn't.</summary>
        private EquatableMap<string, IrFold> Folds(Dictionary<string, IrObjectType> types, List<IrObject> objects)
        {
            var named = types.Values.SelectMany(t => t.Fields.Select(f => f.Type))
                .Concat(objects.SelectMany(o => o.Parameters.Select(p => p.Type).Concat(o.Results?.Select(r => r.Type) ?? [])))
                .SelectMany(IrTypeTree.Within)
                .OfType<IrScalar>()
                .Select(s => s.Facets?.Values?.Fold)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal);
            return named.ToEquatableMap(f => f, f => candidate.Folds.TryGetValue(f, out var fold) ? fold : baseline.Folds[f]);
        }
    }
}
