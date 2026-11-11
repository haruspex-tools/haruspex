using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Haruspex.Ir;

/// <summary>
/// An immutable sequence that is equal to another when it holds equal items in the same order, so that a record holding
/// one compares by value. Every list in the IR, and in the inference's shapes and values, is one of these.
/// </summary>
/// <remarks>
/// A record's generated equality compares each field with that field's own equality. For <see cref="IReadOnlyList{T}"/>
/// and the other collection types of .NET that is the reference (for <see cref="ImmutableArray{T}"/>, the array it wraps),
/// so two records describing the same thing, with lists built apart, are not equal. This type gives a list the equality its
/// items have: <see cref="Equals(EquatableArray{T})"/> compares item by item, and <see cref="GetHashCode"/> combines the
/// items' hashes, so equal sequences hash equal. A collection expression (<c>[a, b]</c>, <c>[.. items]</c>) builds one.
/// It is a class rather than a struct so that a list that may be missing stays a nullable reference, as it was, with no
/// default value that is neither empty nor missing.
/// </remarks>
/// <typeparam name="T">The item type, whose own equality decides the sequence's.</typeparam>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
#pragma warning disable CA1710 // The name says what it is, an array that compares by value; "Collection" would say neither.
public sealed class EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
#pragma warning restore CA1710
{
    internal EquatableArray(ImmutableArray<T> items)
    {
        Items = items;
    }

    /// <summary>The items as an immutable array.</summary>
    public ImmutableArray<T> Items { get; }

    /// <inheritdoc/>
    public int Count => Items.Length;

    /// <inheritdoc/>
    public T this[int index] => Items[index];

    /// <summary>Whether <paramref name="other"/> holds equal items, by each item's own equality, in the same order.</summary>
    public bool Equals(EquatableArray<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as EquatableArray<T>);

    /// <summary>The items' hashes combined in order, so that equal sequences hash equal.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    /// <summary>Whether two sequences hold equal items in the same order; two missing sequences are equal.</summary>
    public static bool operator ==(EquatableArray<T>? left, EquatableArray<T>? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Whether two sequences differ in an item or in length, or only one is missing.</summary>
    public static bool operator !=(EquatableArray<T>? left, EquatableArray<T>? right) => !(left == right);

    /// <summary>An enumerator over the items that does not allocate.</summary>
    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}

/// <summary>Builds an <see cref="EquatableArray{T}"/>; a collection expression calls <see cref="Create{T}"/>.</summary>
public static class EquatableArray
{
    /// <summary>A sequence of <paramref name="items"/>, copied.</summary>
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items) => items.IsEmpty ? Empty<T>.Sequence : new([.. items]);

    /// <summary>One empty sequence per item type, shared, since an empty list is the most common one in the IR.</summary>
    private static class Empty<T>
    {
        public static readonly EquatableArray<T> Sequence = new([]);
    }
}
