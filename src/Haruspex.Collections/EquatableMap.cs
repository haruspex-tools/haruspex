using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Haruspex.Collections;

/// <summary>
/// An immutable map that is equal to another when it holds the same keys, each with an equal value, in whatever order they
/// were added, so that a record holding one compares by value (<see cref="EquatableArray{T}"/> says why records need it).
/// </summary>
/// <remarks>
/// <para>
/// Keys compare by their own equality, always: the map takes no comparer. A key whose equality is not the default one for
/// its values says so in its own type (a SQL name that ignores case is a type that compares ignoring case), so two maps can
/// never disagree about which keys are the same, and equality and hashing need nothing but the entries.
/// </para>
/// <para>
/// The hash adds each entry's hash, so it doesn't depend on the order the entries were added or are enumerated in, which
/// <see cref="ImmutableDictionary{TKey, TValue}"/> doesn't promise; equal maps hash equal.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The key type, whose own equality says which keys are the same.</typeparam>
/// <typeparam name="TValue">The value type, whose own equality decides the map's with the keys'.</typeparam>
[CollectionBuilder(typeof(EquatableMap), nameof(EquatableMap.Create))]
#pragma warning disable CA1710 // The name says what it is, a map that compares by value; "Dictionary" would say neither.
public sealed class EquatableMap<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IEquatable<EquatableMap<TKey, TValue>>
#pragma warning restore CA1710
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TValue> items;

    internal EquatableMap(ImmutableDictionary<TKey, TValue> items)
    {
        this.items = items;
    }

    /// <inheritdoc/>
    public int Count => items.Count;

    /// <inheritdoc/>
    public TValue this[TKey key] => items[key];

    /// <inheritdoc/>
    public IEnumerable<TKey> Keys => items.Keys;

    /// <inheritdoc/>
    public IEnumerable<TValue> Values => items.Values;

    /// <summary>This map with <paramref name="key"/> holding <paramref name="value"/>, in place of what it held.</summary>
    public EquatableMap<TKey, TValue> SetItem(TKey key, TValue value) => new(items.SetItem(key, value));

    /// <summary>This map without <paramref name="key"/>.</summary>
    public EquatableMap<TKey, TValue> Remove(TKey key) => new(items.Remove(key));

    /// <inheritdoc/>
    public bool ContainsKey(TKey key) => items.ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => items.TryGetValue(key, out value);

    /// <summary>What <paramref name="key"/> holds, or the type's default when it holds nothing.</summary>
    public TValue? GetValueOrDefault(TKey key) => items.GetValueOrDefault(key);

    /// <summary>Whether <paramref name="other"/> holds the same keys, each with an equal value by the value's own equality.</summary>
    public bool Equals(EquatableMap<TKey, TValue>? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Count == other.Count && items.All(entry => other.items.TryGetValue(entry.Key, out var value) && EqualityComparer<TValue>.Default.Equals(entry.Value, value))));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as EquatableMap<TKey, TValue>);

    /// <summary>The sum of the entries' hashes, which doesn't depend on their order, so that equal maps hash equal.</summary>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in items)
            hash = unchecked(hash + HashCode.Combine(key, value));
        return hash;
    }

    /// <summary>Whether two maps hold the same entries; two missing maps are equal.</summary>
    public static bool operator ==(EquatableMap<TKey, TValue>? left, EquatableMap<TKey, TValue>? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two maps differ in a key or a value, or only one is missing.</summary>
    public static bool operator !=(EquatableMap<TKey, TValue>? left, EquatableMap<TKey, TValue>? right) => !(left == right);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Builds an <see cref="EquatableMap{TKey, TValue}"/>; a collection expression of entries calls <see cref="Create{TKey, TValue}"/>.</summary>
public static class EquatableMap
{
    /// <summary>A map of <paramref name="entries"/>; a key given twice holds its last value.</summary>
    public static EquatableMap<TKey, TValue> Create<TKey, TValue>(ReadOnlySpan<KeyValuePair<TKey, TValue>> entries)
        where TKey : notnull
    {
        if (entries.IsEmpty)
            return Empty<TKey, TValue>.Map;
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        foreach (var (key, value) in entries)
            builder[key] = value;
        return new(builder.ToImmutable());
    }

    /// <summary>A map of each item under the key <paramref name="key"/> gives it, holding what <paramref name="value"/> gives.</summary>
    public static EquatableMap<TKey, TValue> ToEquatableMap<TSource, TKey, TValue>(this IEnumerable<TSource> items, Func<TSource, TKey> key, Func<TSource, TValue> value)
        where TKey : notnull =>
        Create([.. items.Select(item => KeyValuePair.Create(key(item), value(item)))]);

    /// <summary>One empty map per key and value type, shared.</summary>
    private static class Empty<TKey, TValue>
        where TKey : notnull
    {
        public static readonly EquatableMap<TKey, TValue> Map = new([]);
    }
}
