using System.Collections.Concurrent;
using System.Text;
using Haruspex.Collections;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// What a string gives back once it has been text of a <c>varchar</c> or <c>char</c> type: the one rule for every read of JSON held
/// in such text, a declared column's (<see cref="DeclaredColumns"/>), a declared parameter's (<see cref="DeclaredParameters"/>) and
/// <c>OPENJSON … WITH</c>'s <c>varchar</c> columns (<c>Typer.Converted</c>) and a literal without <c>N</c> (<c>JsonLiterals</c>)
/// (docs/decisions/0145-varchar-json-is-read-through-its-code-page.md).
/// </summary>
/// <remarks>
/// <para>
/// Such text is in its collation's code page, and a character the code page lacks, written as itself, comes back as another: a
/// best fit (<c>Ω</c> as <c>O</c> under 1252, <c>é</c> as <c>e</c> under 1251) or <c>?</c>, and a character outside the Basic
/// Multilingual Plane as <c>??</c>. A JSON escape (<c>\u03a9</c>) is ASCII, so it is kept and read back as the character. A writer
/// may use either, so a value is known to come back unchanged only when the code page holds every character of it. Observed in
/// <c>CodePageObservationTests</c>: on the server, a character survives the round trip through each code page SQL Server has
/// exactly when it survives .NET's encoding of that code page with no fallback, for every character of the Basic Multilingual
/// Plane; each comes back as one UTF-16 unit, and a pair outside it as two; under a <c>_UTF8</c> collation every character comes
/// back, and a lone surrogate as U+FFFD.
/// </para>
/// <para>
/// So a value set holds when every value is held. Without it, the count of UTF-16 units is what it was, and each is one character
/// of the Basic Multilingual Plane: a least length holds, and a greatest one of n code points becomes 2n (one comes back as two),
/// except under UTF-8, where it holds. Where the collation isn't known, ASCII is the floor: every code page holds it.
/// </para>
/// </remarks>
internal static class CodePageText
{
    private static readonly ConcurrentDictionary<int, Encoding> Encodings = new();

    /// <summary>
    /// Whether text of a collation <paramref name="collation"/> gives <paramref name="text"/> back unchanged: every character is
    /// one its code page holds. Of a collation not known (null, or no name SQL Server has), only ASCII.
    /// </summary>
    public static bool Holds(string? collation, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (collation is null ? null : CollationCatalogue.CodePage(collation))
        {
            case null or CollationCatalogue.UnicodeOnly:
                return Ascii.IsValid(text);
            case CollationCatalogue.Utf8:
                return WellFormed(text);
            case { } codePage:
                if (Ascii.IsValid(text))
                    return true;
                var encoding = Encodings.GetOrAdd(codePage, cp => CodePagesEncodingProvider.Instance.GetEncoding(cp, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                    ?? throw new InvalidOperationException($"No encoding of code page {cp}."));
                try
                {
                    return encoding.GetString(encoding.GetBytes(text)) == text;
                }
                catch (EncoderFallbackException)
                {
                    return false;
                }
        }
    }

    /// <summary>Whether every value of <paramref name="values"/> comes back unchanged (<see cref="Holds"/>); false when there is no set.</summary>
    public static bool Keeps(IrValueSet? values, string? collation) => values is { } set && set.Items.All(v => Holds(collation, v.Text));

    /// <summary>
    /// <paramref name="scalar"/>, a string of JSON held as text of a collation <paramref name="collation"/>, as it is read back: its
    /// value set kept only when every value is held; otherwise its least length, and its greatest doubled where it counts code points
    /// (as it stands under UTF-8).
    /// </summary>
    public static IrScalar Read(IrScalar scalar, string? collation)
    {
        ArgumentNullException.ThrowIfNull(scalar);
        if (scalar is not { Kind: IrScalarKind.String, Facets: { } facets } || (facets.Values is not null && Keeps(facets.Values, collation)))
            return scalar;
        var utf8 = collation is not null && CollationCatalogue.CodePage(collation) == CollationCatalogue.Utf8;
        return scalar with { Facets = facets with { Values = null, Length = utf8 ? facets.Length : Doubled(facets.Length) } };
    }

    /// <summary>A greatest length after a code page: in UTF-16 units, the same; in code points, twice as many; in bytes, not known.</summary>
    private static IrLength? Doubled(IrLength? length) => length switch
    {
        { Unit: IrLengthUnit.Utf16 } => length with { IsFixed = false },
        { Unit: IrLengthUnit.CodePoints, Value: <= int.MaxValue / 2 } => length with { Value = 2 * length.Value, IsFixed = false },
        _ => null,
    };

    /// <summary>
    /// Declared JSON, <paramref name="content"/> and the object types it refers to, as text of <paramref name="collation"/> gives it
    /// back: each string as <see cref="Read(IrScalar, string?)"/> says, and each object as <see cref="Keys"/> does.
    /// </summary>
    public static (IrType Content, EquatableMap<string, IrObjectType> Types) Read(IrType content, EquatableMap<string, IrObjectType> types, string? collation)
    {
        ArgumentNullException.ThrowIfNull(types);
        IrScalar Scalar(IrScalar s) => Read(s, collation);
        return (
            IrTypeTree.MapScalars(content, Scalar),
            types.ToEquatableMap(t => t.Key, t => Keys(t.Value with { Fields = [.. t.Value.Fields.Select(f => f with { Type = IrTypeTree.MapScalars(f.Type, Scalar) })] }, collation)));
    }

    /// <summary>
    /// An object whose keys are text of <paramref name="collation"/>: a key the code page doesn't hold, written as itself, comes back
    /// as another key of as many UTF-16 units (observed by ann3-review: <c>{"Ω":"x"}</c> under 1252 is <c>{"O":"x"}</c>). So such a
    /// key may be missing (a path to it, written as itself or as an escape, finds nothing); the object may hold a key it doesn't
    /// list (open); and a key it lists of the same length may be written twice, its value either one (a path reads the first, and
    /// <c>JSON.parse</c> the last), so its type is the join of the two.
    /// </summary>
    private static IrObjectType Keys(IrObjectType type, string? collation)
    {
        var lost = type.Fields.Where(f => !Holds(collation, f.Name)).ToList();
        if (lost.Count == 0)
            return type;
        return type with
        {
            Open = true,
            Fields = [.. type.Fields.Select(field =>
            {
                if (lost.Contains(field))
                    return field with { Presence = IrPresence.Optional };
                var twins = lost.Where(l => l.Name.Length == field.Name.Length).ToList();
                return twins.Count == 0
                    ? field
                    : field with
                    {
                        Type = Union([field.Type, .. twins.Select(t => t.Type)]),
                        NullableBecause = [.. field.NullableBecause.Concat(twins.SelectMany(t => t.NullableBecause)).Distinct()],
                    };
            })],
        };
    }

    /// <summary>The types as one: a union of every distinct variant, or the type itself when there is one.</summary>
    private static IrType Union(IEnumerable<IrType> types)
    {
        var variants = types.SelectMany(t => t is IrUnion union ? union.Variants.AsEnumerable() : [t]).Distinct().ToList();
        return variants.Count == 1 ? variants[0] : new IrUnion([.. variants]);
    }

    /// <summary>Whether every surrogate in <paramref name="text"/> is one of a pair: a lone one comes back from UTF-8 as U+FFFD.</summary>
    private static bool WellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            else if (char.IsSurrogate(text[i]))
                return false;
        }

        return true;
    }
}
