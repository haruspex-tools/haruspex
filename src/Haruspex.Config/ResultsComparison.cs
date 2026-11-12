using System.Globalization;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Compares declared results with inferred ones, for the rule that a declaration of results haruspex can infer is an
/// error when it disagrees and a warning when it agrees (it can be removed). They are compared as far as a declaration can
/// say anything: how many result sets, each one's encoding and whether it may be absent, and in every value its kind, its
/// keys, whether it may be null and whether a key may be left out. Facets (lengths, ranges, value sets) are not compared:
/// a declaration has none.
/// </summary>
internal static class ResultsComparison
{
    /// <summary>Where and how the two first differ, as "path: what each says", or null when they agree.</summary>
    public static string? Difference(
        IReadOnlyList<IrResult> inferred,
        IReadOnlyDictionary<string, IrObjectType> inferredTypes,
        ResultsDeclaration declared)
    {
        if (inferred.Count != declared.Results.Count)
            return $"results: the inference says {Count(inferred.Count)}, the declaration {declared.Results.Count.ToString(CultureInfo.InvariantCulture)}.";

        for (var i = 0; i < inferred.Count; i++)
        {
            var path = $"results[{i.ToString(CultureInfo.InvariantCulture)}]";
            var (a, b) = (inferred[i], declared.Results[i]);
            if (a.Encoding != b.Encoding)
                return $"{path}: the inference says {Encoding(a.Encoding)}, the declaration {Encoding(b.Encoding)}.";
            if (a.MayBeAbsent != b.MayBeAbsent)
                return $"{path}: the inference says it {(a.MayBeAbsent ? "may be absent" : "is always there")}, the declaration that it {(b.MayBeAbsent ? "may be absent" : "is always there")}.";
            if (new Comparer(inferredTypes, declared.Types).Type(a.Type, b.Type, path) is { } difference)
                return difference;
        }

        return null;
    }

    private static string Count(int n) => n == 1 ? "1 result set" : $"{n.ToString(CultureInfo.InvariantCulture)} result sets";

    private static string Encoding(IrEncoding encoding) => encoding == IrEncoding.Rowset ? "a rowset" : "JSON";

    private sealed class Comparer(IReadOnlyDictionary<string, IrObjectType> inferred, IReadOnlyDictionary<string, IrObjectType> declared)
    {
        public string? Type(IrType a, IrType b, string path)
        {
            return (a, b) switch
            {
                (IrUnknownType unknown, _) => $"{path}: the inference can't type it ({IrWireNames.Of(unknown.Reason)}), so it can't agree with a declaration. To type one value, override its path under \"paths\" with \"type\" and \"import\".",
                (IrScalar x, IrScalar y) when Same(x, y) => null,
                // JSON text is a string, which is all a declaration can say of it.
                (IrJsonText, IrScalar { Kind: IrScalarKind.String, Format: null }) => null,
                (IrArray x, IrArray y) => Nulls(x.ItemsNullableBecause.Count > 0, y.ItemsNullableBecause.Count > 0, path + "[]") ?? Type(x.Items, y.Items, path + "[]"),
                (IrRecord x, IrRecord y) => Nulls(x.ValuesNullableBecause.Count > 0, y.ValuesNullableBecause.Count > 0, path + "{}") ?? Type(x.Values, y.Values, path + "{}"),
                (IrRef x, IrRef y) => Object(inferred[x.Name], declared[y.Name], path),
                _ => $"{path}: the inference says {Describe(a)}, the declaration {Describe(b)}.",
            };
        }

        private string? Object(IrObjectType a, IrObjectType b, string path)
        {
            var keys = a.Fields.Select(f => f.Name).Order(StringComparer.Ordinal).ToList();
            var declaredKeys = b.Fields.Select(f => f.Name).Order(StringComparer.Ordinal).ToList();
            if (!keys.SequenceEqual(declaredKeys, StringComparer.Ordinal))
                return $"{path}: the inference says the keys {Keys(keys)}, the declaration {Keys(declaredKeys)}.";

            foreach (var x in a.Fields)
            {
                var y = b.Fields.Single(f => f.Name == x.Name);
                var at = path + IrPath.Key(x.Name);

                // A key that is never null is always there, whatever its presence says.
                var (left, kept) = (x.Nullable && x.Presence == IrPresence.OmittedWhenNull, y.Nullable && y.Presence == IrPresence.OmittedWhenNull);
                if (left != kept)
                    return $"{at}: the inference says the key {(left ? "may be left out" : "is always there")}, the declaration that it {(kept ? "may be left out" : "is always there")}.";
                if (!left && Nulls(x.Nullable, y.Nullable, at) is { } nulls)
                    return nulls;
                if (Type(x.Type, y.Type, at) is { } difference)
                    return difference;
            }

            return null;
        }

        private static string? Nulls(bool a, bool b, string path) => a == b
            ? null
            : $"{path}: the inference says it {(a ? "may be null" : "is never null")}, the declaration that it {(b ? "may be null" : "is never null")}.";

        /// <summary>The same kind; a declared format, where there is one, the same as the inferred.</summary>
        private static bool Same(IrScalar a, IrScalar b) =>
            a.Kind == b.Kind && (b.Format is null ? a.Kind != IrScalarKind.Datetime : b.Format == a.Format);

        private static string Keys(List<string> keys) => keys.Count == 0 ? "none" : string.Join(", ", keys.Select(k => $"\"{k}\""));

        private static string Describe(IrType type) => type switch
        {
            IrScalar { Format: { } format } scalar => $"a {IrWireNames.Of(scalar.Kind)} ({IrWireNames.Of(format)})",
            IrScalar scalar => $"a {IrWireNames.Of(scalar.Kind)}",
            IrRef => "an object",
            IrArray => "an array",
            IrTuple => "a tuple",
            IrRecord => "an object whose keys are data",
            IrUnion => "a value of several types",
            IrJsonText => "JSON text",
            IrImport import => $"the imported {import.Name}",
            _ => "a value",
        };
    }
}
