using System.Text.RegularExpressions;

namespace Haruspex.Config;

/// <summary>One <c>naming.replace</c> rule: a regular expression and its replacement.</summary>
/// <param name="Pattern">The compiled expression.</param>
/// <param name="With">The replacement, where <c>$1</c> names a group.</param>
/// <param name="At">Where the rule is written.</param>
public sealed record NamingReplacement(Regex Pattern, string With, ConfigLocation? At);
