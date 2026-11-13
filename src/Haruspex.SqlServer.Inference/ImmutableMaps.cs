using System.Collections.Immutable;
using Haruspex.Collections;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Equality and hashing of an immutable dictionary by its entries, for the states and contexts that hold one: .NET's
/// dictionaries compare by reference, as its lists do (<see cref="EquatableArray{T}"/> says why that matters).
/// </summary>
internal static class ImmutableMaps
{
    /// <summary>
    /// Whether two maps hold the same keys, by <paramref name="b"/>'s key comparer, each with an equal value. The maps here
    /// are keyed by SQL names, which compare ignoring case, so <c>#Lines</c> and <c>#lines</c> are one key.
    /// </summary>
    public static bool Equal<TValue>(ImmutableDictionary<string, TValue> a, ImmutableDictionary<string, TValue> b) =>
        ReferenceEquals(a, b)
        || (a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var other) && EqualityComparer<TValue>.Default.Equals(entry.Value, other)));

    /// <summary>A hash of the entries that doesn't depend on their order, with each key hashed by the map's comparer.</summary>
    public static int Hash<TValue>(ImmutableDictionary<string, TValue> map)
    {
        var hash = 0;
        foreach (var (key, value) in map)
            hash += HashCode.Combine(map.KeyComparer.GetHashCode(key), value);
        return hash;
    }
}
