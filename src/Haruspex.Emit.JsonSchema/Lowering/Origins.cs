using Haruspex.Collections;
using Haruspex.Emit.JsonSchema.Model;
using Haruspex.Ir;

namespace Haruspex.Emit.JsonSchema.Lowering;

/// <summary>
/// The IR's origins as notes: why a value may be null, and where each fact about a scalar came from, so that a reader of the
/// schema (a person, an editor, an MCP catalogue) sees what the database guarantees and why, not only the keywords.
/// </summary>
internal static class Origins
{
    /// <summary>
    /// Why a value may be null, or a key be left out, in the IR's vocabulary (<c>nullable-column (shop.basket.note)</c>); null
    /// when there is no reason, which is the only way the IR says never null.
    /// </summary>
    /// <param name="lead">What the reasons are for, such as <c>May be null</c>.</param>
    public static SchemaNote? Nulls(string lead, EquatableArray<IrNullReason> reasons) =>
        reasons.Count == 0
            ? null
            : new SchemaNote($"{lead}: {string.Join(", ", reasons.Select(r => $"{IrWireNames.Of(r.Rule)} ({r.At})").Distinct(StringComparer.Ordinal))}.");

    /// <summary>
    /// A scalar's SQL type, and where each of its facts came from when that isn't the type itself: a CHECK constraint, an alias
    /// type, the engine or an expression. A fact from the SQL type goes without saying once the type is said.
    /// </summary>
    public static IEnumerable<SchemaNote> Of(IrScalar scalar)
    {
        if (scalar.Sql is { } sql)
            yield return new SchemaNote($"SQL type {sql}.");
        if (scalar.Facets is not { } facets)
            yield break;

        IEnumerable<(string Fact, IrFactSource From)> facts =
        [
            .. Fact(facets.Length?.IsFixed == true ? "length" : "maxLength", facets.Length?.From),
            .. Fact("minLength", facets.MinLength?.From),
            .. Fact("digits", facets.Digits?.From),
            .. Fact("min", facets.Min?.From),
            .. Fact("max", facets.Max?.From),
            .. (facets.Values?.From ?? []).Select(from => ("values", from)),
        ];
        var sources = facts
            .Where(f => !(f.From.Kind == IrFactSourceKind.Type && string.Equals(f.From.Ref, scalar.Sql, StringComparison.Ordinal)))
            .GroupBy(f => Source(f.From), StringComparer.Ordinal)
            .Select(g => $"{Listed([.. g.Select(f => f.Fact).Distinct(StringComparer.Ordinal)])} from {g.Key}")
            .ToList();
        if (sources.Count > 0)
            yield return new SchemaNote($"Facts: {string.Join("; ", sources)}.");
    }

    private static IEnumerable<(string, IrFactSource)> Fact(string fact, IrFactSource? from) => from is null ? [] : [(fact, from)];

    private static string Source(IrFactSource from) => from.Kind switch
    {
        IrFactSourceKind.Type => $"the type {from.Ref}",
        IrFactSourceKind.Check => $"the check {from.Ref}",
        IrFactSourceKind.Engine => $"the engine ({from.Ref})",
        IrFactSourceKind.Expression => $"the expression at {from.Ref}",
        IrFactSourceKind.Declared => $"the declaration of {from.Ref}",
    };

    /// <summary>Items as an English list: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    public static string Listed(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
