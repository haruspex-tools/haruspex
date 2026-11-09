using System.Data.SqlTypes;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haruspex.Ir;

namespace Haruspex.DifferentialTests.Validation;

/// <summary>
/// Checks a procedure's real output against the IR inferred for it, claim by claim.
/// </summary>
/// <remarks>
/// It reads the IR directly and never goes through an emitter, so it cannot share an emitter's mistakes: a
/// wrong Zod mapping would otherwise pass its own test. It checks only what the IR claims: that a non-null
/// field is never null, that each key present is one the type has and each key that must be there is, that a
/// result said always to be present is, that each scalar has the form its kind claims, and that each scalar keeps
/// its facets (lengths, digits, bounds and value sets; see <see cref="FacetProblem"/>). It does not check what
/// the IR leaves open, such as fractional-second precision.
/// </remarks>
public static partial class IrValidator
{
    public static ValidationReport Validate(IrDocument document, string procedure, IReadOnlyList<ObservedResultSet> results)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(results);
        var target = document.Objects.SingleOrDefault(o => o.Id == procedure)
            ?? throw new ArgumentException($"The IR has no object {procedure}.", nameof(procedure));

        var run = new Run(document, procedure);

        // Results that cannot be listed claim nothing, so any output is consistent with them.
        if (target.Results is not { } claimed)
            return run.Report();

        if (results.Count != claimed.Count)
        {
            run.Violation("results", $"{claimed.Count} result set(s)", results.Count.ToString(CultureInfo.InvariantCulture));
            return run.Report();
        }

        for (var i = 0; i < claimed.Count; i++)
        {
            var path = $"results[{i}]";
            if (claimed[i].Encoding == IrEncoding.Json)
                run.JsonResult(claimed[i], results[i], path);
            else
                run.RowsetResult(claimed[i], results[i], path);
        }

        return run.Report();
    }

    private sealed class Run(IrDocument document, string procedure)
    {
        private readonly List<Violation> violations = [];
        private readonly HashSet<(string, Seen)> seen = [];
        private readonly HashSet<(string, string)> edges = [];

        public ValidationReport Report() => new(violations, seen, edges);

        /// <summary>Checks a non-null scalar's facets, recording a violation and the edges it reaches.</summary>
        private void Facets(IrScalar scalar, ScalarValue value, string path, string general)
        {
            if (FacetProblem(scalar, value, e => edges.Add((general, e))) is { } claim)
                Violation(path, claim, value.Describe());
        }

        public void Violation(string path, string claim, string value) => violations.Add(new Violation(procedure, path, claim, value));

        // ---- JSON -----------------------------------------------------------------------------------

        public void JsonResult(IrResult result, ObservedResultSet set, string path)
        {
            // FOR JSON returns one column, and splits long output over several rows, which the caller joins.
            if (set.Columns.Count != 1)
            {
                Violation(path, "a FOR JSON result of one column", $"{set.Columns.Count} columns");
                return;
            }

            if (set.Rows.Count == 0)
            {
                seen.Add((path, Seen.Absent));
                if (!result.MayBeAbsent)
                    Violation(path, "a result that is always present", "(absent: no rows)");
                return;
            }

            var text = new StringBuilder();
            foreach (var row in set.Rows)
                text.Append(row[0] as string);

            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(text.ToString());
            }
            catch (JsonException e)
            {
                Violation(path, "valid JSON", $"{Shorten(text.ToString())} ({e.Message})");
                return;
            }

            using (json)
            {
                seen.Add((path, Seen.Value));
                Json(result.Type, json.RootElement, path, path);
            }
        }

        /// <param name="path">The path with array indexes, for messages.</param>
        /// <param name="general">The path with <c>[]</c> for array items, for <see cref="Seen"/>.</param>
        private void Json(IrType type, JsonElement value, string path, string general)
        {
            switch (type)
            {
                case IrUnknownType:
                    return;
                case IrRef reference:
                    JsonObject(document.Types[reference.Name], value, path, general);
                    return;
                case IrArray array:
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        Violation(path, "an array", Shorten(value.GetRawText()));
                        return;
                    }

                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Null)
                            Violation($"{path}[{index}]", "array items that are never null", "null");
                        else
                            Json(array.Items, item, $"{path}[{index}]", $"{general}[]");
                        index++;
                    }

                    return;
                case IrJsonText:
                    if (value.ValueKind != JsonValueKind.String || !ParsesAsJson(value.GetString()!))
                        Violation(path, "a string holding JSON", Shorten(value.GetRawText()));
                    return;
                case IrScalar scalar:
                    if (JsonScalarProblem(scalar, value) is { } claim)
                        Violation(path, claim, value.GetRawText());
                    else if (ScalarValue.FromJson(scalar, value) is { } facetValue)
                        Facets(scalar, facetValue, path, general);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown IR type {type}.");
            }
        }

        private void JsonObject(IrObjectType type, JsonElement value, string path, string general)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                Violation(path, "an object", Shorten(value.GetRawText()));
                return;
            }

            var present = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                // FOR JSON keys are compared by case, as JavaScript compares them.
                if (!present.TryAdd(property.Name, property.Value))
                    Violation($"{path}.{property.Name}", "each key at most once", Shorten(property.Value.GetRawText()));
                else if (!type.Fields.Any(f => f.Name.Equals(property.Name, StringComparison.Ordinal)))
                    Violation($"{path}.{property.Name}", $"no key {property.Name}: the keys are {string.Join(", ", type.Fields.Select(f => f.Name))}", Shorten(property.Value.GetRawText()));
            }

            foreach (var field in type.Fields)
            {
                var fieldPath = $"{path}.{field.Name}";
                var fieldGeneral = $"{general}.{field.Name}";
                var known = field.Type is not IrUnknownType;

                if (!present.TryGetValue(field.Name, out var fieldValue))
                {
                    if (field.Presence == IrPresence.Always)
                        Violation(fieldPath, "a key that is always present", "(absent)");
                    else if (known)
                        SeenNull(field.Type, fieldGeneral);
                    continue;
                }

                if (fieldValue.ValueKind == JsonValueKind.Null)
                {
                    if (!field.Nullable)
                        Violation(fieldPath, "a non-null value", "null");
                    else if (field.Presence == IrPresence.OmittedWhenNull)
                        Violation(fieldPath, "a key omitted when null, so never null when present", "null");
                    else if (known)
                        SeenNull(field.Type, fieldGeneral);
                    continue;
                }

                if (known)
                    seen.Add((fieldGeneral, Seen.Value));
                Json(field.Type, fieldValue, fieldPath, fieldGeneral);
            }
        }

        /// <summary>
        /// Records a null at <paramref name="general"/> and, for a nested object, at each field inside it. FOR
        /// JSON leaves an object out when its values are all null (the columns of a left join, say), so a field in
        /// it is never seen null on its own; the object's absence is the data making each of them null.
        /// </summary>
        private void SeenNull(IrType type, string general)
        {
            seen.Add((general, Seen.Null));
            if (type is not IrRef reference)
                return;
            foreach (var field in document.Types[reference.Name].Fields)
            {
                if (field.Type is not IrUnknownType)
                    SeenNull(field.Type, $"{general}.{field.Name}");
            }
        }

        // ---- rowsets --------------------------------------------------------------------------------

        public void RowsetResult(IrResult result, ObservedResultSet set, string path)
        {
            seen.Add((path, Seen.Value));
            if (result.Type is IrUnknownType)
                return;
            if (result.Type is not IrArray { Items: IrRef row })
                throw new InvalidOperationException($"A rowset's type should be an array of rows, not {result.Type}.");

            var fields = document.Types[row.Name].Fields;
            for (var r = 0; r < set.Rows.Count; r++)
            {
                var rowPath = $"{path}[{r}]";
                for (var c = 0; c < set.Columns.Count; c++)
                {
                    if (!fields.Any(f => f.Name.Equals(set.Columns[c], StringComparison.Ordinal)))
                        Violation($"{rowPath}.{set.Columns[c]}", $"no column {set.Columns[c]}: the columns are {string.Join(", ", fields.Select(f => f.Name))}", Describe(set.Rows[r][c]));
                }

                foreach (var field in fields)
                {
                    var fieldPath = $"{rowPath}.{field.Name}";
                    var c = IndexOf(set.Columns, field.Name);
                    if (c < 0)
                    {
                        Violation(fieldPath, "a column that is always present", "(absent)");
                        continue;
                    }

                    if (field.Type is IrUnknownType)
                        continue;

                    var value = set.Rows[r][c];
                    if (value is null)
                    {
                        if (!field.Nullable)
                            Violation(fieldPath, "a non-null value", "null");
                        else
                            seen.Add(($"{path}[].{field.Name}", Seen.Null));
                        continue;
                    }

                    seen.Add(($"{path}[].{field.Name}", Seen.Value));
                    if (RowsetValueProblem(field.Type, value) is { } claim)
                        Violation(fieldPath, claim, Describe(value));
                    else if (field.Type is IrScalar scalar && ScalarValue.FromRowset(value) is { } facetValue)
                        Facets(scalar, facetValue, fieldPath, $"{path}[].{field.Name}");
                }
            }
        }

        private static int IndexOf(IReadOnlyList<string> columns, string name)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].Equals(name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }

    // ---- facets -------------------------------------------------------------------------------------

    /// <summary>
    /// What a facet claims, when <paramref name="value"/> breaks it; otherwise null. Each facet is checked in the
    /// unit it states: a length in UTF-16 code units, or in bytes of UTF-8 when the IR says so (and otherwise, for
    /// bytes, only as the upper bound on UTF-16 code units it always is); bounds and digits exactly, from the decimal
    /// text, never through a double; an exact set by equality, and a set under a collation ignoring case and
    /// trailing spaces, which is what the fixtures' collations ignore. It also reports, through
    /// <paramref name="edge"/>, each edge the value reaches (<see cref="ValidationReport.Edges"/>).
    /// </summary>
    internal static string? FacetProblem(IrScalar scalar, ScalarValue value, Action<string> edge)
    {
        if (scalar.Facets is not { } facets)
            return null;

        if (value.Text is { } text)
        {
            if (facets.Length is { } length)
            {
                var measure = length is { Unit: IrLengthUnit.Bytes, Encoding: IrByteEncoding.Utf8 } ? Encoding.UTF8.GetByteCount(text) : text.Length;
                var exact = length.Unit == IrLengthUnit.Utf16 || length.Encoding is IrByteEncoding.SingleByte or IrByteEncoding.Utf8;
                var unit = length.Unit == IrLengthUnit.Utf16 ? "UTF-16 code units" : length.Encoding == IrByteEncoding.Utf8 ? "UTF-8 bytes" : "characters";
                if (length.IsFixed && exact ? measure != length.Value : measure > length.Value)
                    return $"{(length.IsFixed && exact ? "exactly" : "at most")} {length.Value} {unit} ({scalar.Sql})";
                if (measure == length.Value)
                    edge($"length {length.Value}");
            }

            if (facets.MinLength is { } minLength)
            {
                if (text.Length < minLength.Value)
                    return $"at least {minLength.Value} UTF-16 code units ({scalar.Sql})";
                if (text.Length == minLength.Value)
                    edge($"min length {minLength.Value}");
            }

            if (facets.Values is { } strings)
            {
                var match = strings.Items.FirstOrDefault(i => i.IsString && (strings.Match == IrValueMatch.Exact
                    ? i.Text.Equals(text, StringComparison.Ordinal)
                    : i.Text.TrimEnd(' ').Equals(text.TrimEnd(' '), StringComparison.OrdinalIgnoreCase)));
                if (match is null)
                    return $"one of {string.Join(", ", strings.Items.Select(i => JsonSerializer.Serialize(i.Text)))} ({strings.Match.ToString().ToLowerInvariant()})";
                edge("value " + match.Text);
            }

            return null;
        }

        var number = value.Number!.Value;
        if (facets.Digits is { } digits && (number.Scale > digits.Scale || number.IntegerDigits > digits.Precision - digits.Scale))
            return $"at most {digits.Precision} digits, {digits.Scale} after the point ({scalar.Sql})";

        // The step to the nearest value inside an exclusive bound: the type's scale, or 1 for an integer format.
        int? scale = facets.Digits?.Scale ?? (IntegerRange(scalar.Format) is null ? null : 0);
        if (facets.Min is { } min)
        {
            var bound = Exact.Parse(min.Value);
            if (min.Inclusive ? number.CompareTo(bound) < 0 : number.CompareTo(bound) <= 0)
                return $"{(min.Inclusive ? "at least" : "more than")} {min.Value} ({scalar.Sql})";
            if (min.Inclusive ? number.CompareTo(bound) == 0 : scale is { } s && number.CompareTo(bound.Step(s, 1)) == 0)
                edge("min");
        }

        if (facets.Max is { } max)
        {
            var bound = Exact.Parse(max.Value);
            if (max.Inclusive ? number.CompareTo(bound) > 0 : number.CompareTo(bound) >= 0)
                return $"{(max.Inclusive ? "at most" : "less than")} {max.Value} ({scalar.Sql})";
            if (max.Inclusive ? number.CompareTo(bound) == 0 : scale is { } s && number.CompareTo(bound.Step(s, -1)) == 0)
                edge("max");
        }

        if (facets.Values is { } numbers)
        {
            var match = numbers.Items.FirstOrDefault(i => !i.IsString && Exact.Parse(i.Text).CompareTo(number) == 0);
            if (match is null)
                return $"one of {string.Join(", ", numbers.Items.Select(i => i.Text))}";
            edge("value " + match.Text);
        }

        return null;
    }

    // ---- scalars ------------------------------------------------------------------------------------

    /// <summary>What the scalar claims, when <paramref name="value"/> breaks it; otherwise null.</summary>
    private static string? JsonScalarProblem(IrScalar scalar, JsonElement value)
    {
        var claim = $"{Name(scalar)} ({scalar.Sql})";
        switch (scalar.Kind)
        {
            case IrScalarKind.String:
                return value.ValueKind == JsonValueKind.String ? null : claim;
            case IrScalarKind.Boolean:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : claim;
            case IrScalarKind.Number:
                if (value.ValueKind != JsonValueKind.Number)
                    return claim;
                return IntegerRange(scalar.Format) is not { } range || IsIntegerIn(value.GetRawText(), range) ? null : claim;
            default:
                if (value.ValueKind != JsonValueKind.String)
                    return claim;
                return MatchesFormat(scalar, value.GetString()!) ? null : claim;
        }
    }

    private static string Name(IrScalar scalar) => scalar.Format is null
        ? scalar.Kind.ToString().ToLowerInvariant()
        : $"{scalar.Kind.ToString().ToLowerInvariant()} {scalar.Format}";

    private static (BigInteger Min, BigInteger Max)? IntegerRange(string? format) => format switch
    {
        IrFormats.Uint8 => (byte.MinValue, byte.MaxValue),
        IrFormats.Int16 => (short.MinValue, short.MaxValue),
        IrFormats.Int32 => (int.MinValue, int.MaxValue),
        IrFormats.Int64 => (long.MinValue, long.MaxValue),
        _ => null,
    };

    /// <summary>Read from the JSON text, not as a double, so a 64-bit integer is checked exactly.</summary>
    private static bool IsIntegerIn(string raw, (BigInteger Min, BigInteger Max) range) =>
        IntegerLiteral().IsMatch(raw)
        && BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
        && n >= range.Min && n <= range.Max;

    /// <summary>
    /// The string forms the IR's kinds claim in JSON (src/Haruspex.Ir/README.md, "Scalars in JSON"). Any number of
    /// fractional digits is accepted, because the IR does not claim a precision; the exact strings SQL Server writes
    /// are pinned by ForJsonObservationTests.
    /// </summary>
    private static bool MatchesFormat(IrScalar scalar, string text)
    {
        switch (scalar.Kind)
        {
            case IrScalarKind.Date:
                return DateFormat().IsMatch(text) && ValidDate(text);
            case IrScalarKind.Time:
                return TimeFormat().Match(text) is { Success: true } time && ValidTime(time);
            case IrScalarKind.Datetime when scalar.Format == IrFormats.Local:
                return LocalDatetimeFormat().Match(text) is { Success: true } local && ValidDate(local.Groups["date"].Value) && ValidTime(local);
            case IrScalarKind.Datetime when scalar.Format == IrFormats.Offset:
                return OffsetDatetimeFormat().Match(text) is { Success: true } offset && ValidDate(offset.Groups["date"].Value) && ValidTime(offset) && ValidOffset(offset);
            case IrScalarKind.Datetime:
                // Without a format the IR does not say whether there is an offset.
                return LocalDatetimeFormat().IsMatch(text) || OffsetDatetimeFormat().IsMatch(text);
            case IrScalarKind.Uuid:
                return UuidFormat().IsMatch(text);
            default:
                throw new InvalidOperationException($"Unknown scalar kind {scalar.Kind}.");
        }
    }

    private static bool ValidDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool ValidTime(Match m) =>
        int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) < 24
        && int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) < 60
        && int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture) < 60;

    /// <summary>SQL Server's offsets run from -14:00 to +14:00; ISO 8601 allows no more.</summary>
    private static bool ValidOffset(Match m) =>
        m.Groups["oh"].Value.Length == 0
        || (int.Parse(m.Groups["om"].Value, CultureInfo.InvariantCulture) < 60
            && int.Parse(m.Groups["oh"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups["om"].Value, CultureInfo.InvariantCulture) <= 14 * 60);

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)$")]
    private static partial Regex IntegerLiteral();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex DateFormat();

    [GeneratedRegex(@"^(?<h>[0-9]{2}):(?<m>[0-9]{2}):(?<s>[0-9]{2})(\.[0-9]+)?$")]
    private static partial Regex TimeFormat();

    [GeneratedRegex(@"^(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})T(?<h>[0-9]{2}):(?<m>[0-9]{2}):(?<s>[0-9]{2})(\.[0-9]+)?$")]
    private static partial Regex LocalDatetimeFormat();

    [GeneratedRegex(@"^(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})T(?<h>[0-9]{2}):(?<m>[0-9]{2}):(?<s>[0-9]{2})(\.[0-9]+)?(Z|[+-](?<oh>[0-9]{2}):(?<om>[0-9]{2}))$")]
    private static partial Regex OffsetDatetimeFormat();

    [GeneratedRegex(@"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")]
    private static partial Regex UuidFormat();

    /// <summary>
    /// What the type claims, when a value read by SqlClient breaks it; otherwise null. In a rowset the driver
    /// types each value, so the check is on the .NET type it chose (SqlClient's documented mapping).
    /// </summary>
    private static string? RowsetValueProblem(IrType type, object value)
    {
        switch (type)
        {
            case IrJsonText:
                return value is string s && ParsesAsJson(s) ? null : "a string holding JSON";
            case IrScalar scalar:
                var ok = scalar.Kind switch
                {
                    IrScalarKind.String => value is string,
                    IrScalarKind.Boolean => value is bool,
                    IrScalarKind.Number => scalar.Format switch
                    {
                        IrFormats.Uint8 => value is byte,
                        IrFormats.Int16 => value is short,
                        IrFormats.Int32 => value is int,
                        IrFormats.Int64 => value is long,
                        IrFormats.Decimal => value is decimal or SqlDecimal,
                        IrFormats.Float => value is double or float,
                        _ => value is byte or short or int or long or decimal or SqlDecimal or double or float,
                    },
                    IrScalarKind.Date => value is DateTime { TimeOfDay.Ticks: 0 },
                    IrScalarKind.Time => value is TimeSpan,
                    IrScalarKind.Datetime => scalar.Format switch
                    {
                        IrFormats.Local => value is DateTime,
                        IrFormats.Offset => value is DateTimeOffset,
                        _ => value is DateTime or DateTimeOffset,
                    },
                    IrScalarKind.Uuid => value is Guid,
                    _ => throw new InvalidOperationException($"Unknown scalar kind {scalar.Kind}."),
                };
                return ok ? null : $"{Name(scalar)} ({scalar.Sql})";
            default:
                return $"a scalar column, not {type}";
        }
    }

    private static bool ParsesAsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(Shorten(s)),
        IFormattable f => $"{f.ToString(null, CultureInfo.InvariantCulture)} ({value.GetType().Name})",
        _ => $"{value} ({value.GetType().Name})",
    };

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..197] + "...";
}
