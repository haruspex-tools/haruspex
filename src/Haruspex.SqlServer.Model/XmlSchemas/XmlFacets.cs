using Haruspex.Collections;

namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>
/// The facets a simple type's values satisfy, the tightest of each through every type it restricts, as XML Schema states them: a
/// number as its literal, a length in the server's count, an enumeration's values with the type's whitespace rule applied.
/// </summary>
/// <param name="Enumeration">The values it is drawn from; null when no type restricts it to a set.</param>
/// <param name="MinInclusive">Its least value; null when none.</param>
/// <param name="MinExclusive">The value it is greater than; null when none.</param>
/// <param name="MaxInclusive">Its greatest value; null when none.</param>
/// <param name="MaxExclusive">The value it is less than; null when none.</param>
/// <param name="MinLength">Its least length; null when none.</param>
/// <param name="MaxLength">Its greatest length; null when none.</param>
/// <param name="TotalDigits">Its most digits; null when none.</param>
/// <param name="FractionDigits">Its most digits after the point; null when none.</param>
/// <param name="Pattern">Whether a pattern restricts it, which is not read.</param>
public sealed record XmlFacets(
    EquatableArray<string>? Enumeration = null,
    string? MinInclusive = null,
    string? MinExclusive = null,
    string? MaxInclusive = null,
    string? MaxExclusive = null,
    int? MinLength = null,
    int? MaxLength = null,
    int? TotalDigits = null,
    int? FractionDigits = null,
    bool Pattern = false);
