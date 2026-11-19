using Haruspex.Collections;

namespace Haruspex.Ir;

public static partial class IrCombination
{
    private sealed partial class Combiner
    {
        /// <summary>The combined table types the kept parameters take, by name, each the meet of both definitions.</summary>
        private readonly Dictionary<string, IrTableType> tableTypes = new(StringComparer.Ordinal);

        /// <summary>The combined rows of those table types, by the name of the candidate's row type, which the combined document keeps.</summary>
        private readonly Dictionary<string, IrObjectType> rows = new(StringComparer.Ordinal);

        /// <summary>
        /// A table-valued parameter both procedures have, narrowed as any other parameter is: the rows the combined function sends
        /// are rows both types take. The type must be the one name in both (another, even of the same columns, fails the call,
        /// 206), with the same columns in the same order (they are matched by place) and collations; each column's type is the
        /// narrower of the two and nullable only where both are; and every key and condition of either holds. Null, reported,
        /// when that can't be.
        /// </summary>
        private IrParameter? TableValued(string id, string path, IrParameter old, IrParameter parameter)
        {
            var was = old.TableType is { } a ? baseline.TableTypes.GetValueOrDefault(a) : null;
            var now = parameter.TableType is { } b ? candidate.TableTypes.GetValueOrDefault(b) : null;
            if (parameter.TableType is not { } key || was is null || now is null || !string.Equals(was.Bracketed, now.Bracketed, StringComparison.OrdinalIgnoreCase))
                return NotSent(id, path, $"changed from {Spelt(old, was)} to {Spelt(parameter, now)}, which one function can't send to both");

            // A type no row can be sent in claims nothing, as in a document of one database, and is bridged only unchanged.
            if (old.Type is IrUnknownType || parameter.Type is IrUnknownType)
            {
                if (!(old.Type is IrUnknownType && parameter.Type is IrUnknownType && was.Defines(now)))
                    return NotSent(id, path, $"changed its table type from {IrClaimText.TableType(was)} to {IrClaimText.TableType(now)}, of which no row can be sent to one of the two");
                tableTypes[key] = now;
                return parameter;
            }

            if (was.Columns.Count != now.Columns.Count
                || was.Columns.Zip(now.Columns).Any(c => !string.Equals(c.First.Name, c.Second.Name, StringComparison.OrdinalIgnoreCase) || c.First.Collation != c.Second.Collation))
            {
                return NotSent(id, path, $"changed the columns of its table type from {IrClaimText.TableType(was)} to {IrClaimText.TableType(now)}, which the server matches by place, so one table can't be sent to both");
            }

            var columns = new List<(IrTableColumn Column, IrScalar Type)>();
            for (var i = 0; i < now.Columns.Count; i++)
            {
                var (before, after) = (was.Columns[i], now.Columns[i]);
                var (type, why) = before.Type is { } x && after.Type is { } y ? Narrower(x, y) : (null, "which haruspex doesn't model");
                if (type is not IrScalar scalar)
                    return NotSent(id, path, $"changed the column {after.Name} of its table type from {before.Sql} to {after.Sql}, {why}");
                columns.Add((after with { Type = scalar, Nullable = before.Nullable && after.Nullable, Sql = scalar.Sql ?? after.Sql }, scalar));
            }

            var meet = now with
            {
                Columns = [.. columns.Select(c => c.Column)],
                Checks = [.. now.Checks.Union(was.Checks, StringComparer.Ordinal)],
                Keys = [.. now.Keys.Select(k => was.Keys.FirstOrDefault(o => SameColumns(o, k)) is { } other ? k with { IgnoresDuplicates = k.IgnoresDuplicates && other.IgnoresDuplicates } : k)
                    .Concat(was.Keys.Where(k => !now.Keys.Any(n => SameColumns(n, k))))],
            };
            tableTypes[key] = meet;

            // The row the function sends: the candidate's, each column's value narrowed.
            var row = ((IrRef)((IrArray)parameter.Type).Items).Name;
            rows[row] = candidate.Types[row] with
            {
                Fields = [.. candidate.Types[row].Fields.Zip(columns, (f, c) => f with { Type = c.Type, NullableBecause = c.Column.Nullable ? f.NullableBecause : [] })],
            };
            return parameter;
        }

        private static bool SameColumns(IrTableKey a, IrTableKey b) =>
            a.Columns.Count == b.Columns.Count && a.Columns.All(c => b.Columns.Contains(c, StringComparer.OrdinalIgnoreCase));

        /// <summary>A parameter as its type spells it: a table type's name, or its type otherwise.</summary>
        private static string Spelt(IrParameter parameter, IrTableType? type) => type is not null ? type.Bracketed : Spelt(parameter.Type);

        /// <summary>A change that can't be bridged, reported; null, for a parameter no combined function can send.</summary>
        private IrParameter? NotSent(string id, string path, string what)
        {
            Unbridgeable(id, path, what + ": ship the change in two releases");
            return null;
        }

        /// <summary>The table types the kept objects' parameters take.</summary>
        private EquatableMap<string, IrTableType> TableTypes(List<IrObject> objects)
        {
            var taken = objects.SelectMany(o => o.Parameters).Select(p => p.TableType).OfType<string>().ToHashSet(StringComparer.Ordinal);
            return tableTypes.Where(t => taken.Contains(t.Key)).ToEquatableMap(t => t.Key, t => t.Value);
        }
    }
}
