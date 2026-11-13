using System.Text.RegularExpressions;
using Haruspex.Diagnostics;

namespace Haruspex.Config;

/// <summary>One <c>naming.replace</c> rule: a regular expression and its replacement.</summary>
/// <param name="Pattern">The compiled expression.</param>
/// <param name="With">The replacement, where <c>$1</c> names a group.</param>
/// <param name="At">Where the rule is written.</param>
/// <remarks>
/// A compiled expression compares by reference, so the rule's equality is written here: two rules are the same when their
/// expressions are written alike with the same options, and they replace with the same text from the same place.
/// </remarks>
public sealed record NamingReplacement(Regex Pattern, string With, JsonLocation? At)
{
    /// <summary>Whether <paramref name="other"/> has an expression written alike, with the same options, replacement and place.</summary>
    public bool Equals(NamingReplacement? other) =>
        other is not null
        && string.Equals(Pattern.ToString(), other.Pattern.ToString(), StringComparison.Ordinal)
        && Pattern.Options == other.Pattern.Options
        && string.Equals(With, other.With, StringComparison.Ordinal)
        && At == other.At;

    /// <summary>A hash consistent with <see cref="Equals(NamingReplacement)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Pattern.ToString()), Pattern.Options, With, At);
}
