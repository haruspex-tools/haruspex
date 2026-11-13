using System.Globalization;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Inference.Lattice;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Semantics;
using Haruspex.SqlServer.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Haruspex.SqlServer.Inference;

// OPENJSON and JSON_VALUE over JSON built here: they read a document whose shape is known, so what they give is
// known too. Each rule was observed on SQL Server 2025 RTM-CU10 (JsonTracingObservationTests). See README.md, "JSON built
// here, traced".
internal sealed partial class ProcedureAnalyzer
{
    /// <summary>
    /// The JSON a document argument holds when it was built here: live JSON its shape, JSON text its content, a literal that is a
    /// JSON document its parsed shape; and why it may be NULL. Null when not known (data, or unknown).
    /// </summary>
    private (Shape Shape, EquatableArray<IrNullReason> NullableBecause)? DocumentOf(ScalarExpression document, ReadScope scope)
    {
        if (LiteralOf(document) is ({ } literal and StringLiteral, false))
            return JsonLiterals.Parse(literal.Value, literal is StringLiteral { IsNational: true }, At(literal)) is var parsed and not UnknownShape ? (parsed, []) : null;
        var value = Expression(document, scope);
        var shape = value.IsLiveJson ? value.Shape : value.Shape is JsonTextShape { Content: var content } ? content : null;
        return shape is null or UnknownShape or RecursiveShape ? null : (shape, value.NullableBecause);
    }

    // ---- paths --------------------------------------------------------------------------------------------------------

    /// <summary>A JSON path: whether it is strict, and its steps.</summary>
    private sealed record JsonPath(bool Strict, EquatableArray<JsonStep> Steps);

    /// <summary>A step of a JSON path: a key of an object, or an index into an array.</summary>
    private abstract record JsonStep
    {
        private protected JsonStep()
        {
        }
    }

    /// <summary>A key of an object, as the path writes it.</summary>
    private sealed record JsonKey(string Name) : JsonStep;

    /// <summary>An index into an array, from 0.</summary>
    private sealed record JsonIndex(int Position) : JsonStep;

    /// <summary>
    /// Reads a path such as <c>'strict $.lines[0]."unit price"'</c>: <c>lax</c> or <c>strict</c>, <c>$</c>, then keys and
    /// indexes. Null for anything else (a wildcard, a malformed path), which is not read.
    /// </summary>
    private static JsonPath? ParsePath(string text)
    {
        var s = text.Trim();
        var strict = false;
        if (s.StartsWith("strict ", StringComparison.OrdinalIgnoreCase))
            (strict, s) = (true, s[7..].TrimStart());
        else if (s.StartsWith("lax ", StringComparison.OrdinalIgnoreCase))
            s = s[4..].TrimStart();
        if (s.Length == 0 || s[0] != '$')
            return null;

        var steps = new List<JsonStep>();
        var i = 1;
        while (i < s.Length)
        {
            if (s[i] == '.' && i + 1 < s.Length && s[i + 1] == '"')
            {
                var end = s.IndexOf('"', i + 2);
                if (end < 0 || s[(i + 2)..end].Contains('\\', StringComparison.Ordinal))
                    return null;
                steps.Add(new JsonKey(s[(i + 2)..end]));
                i = end + 1;
            }
            else if (s[i] == '.')
            {
                var start = ++i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '$'))
                    i++;
                if (i == start)
                    return null;
                steps.Add(new JsonKey(s[start..i]));
            }
            else if (s[i] == '[')
            {
                var end = s.IndexOf(']', i);
                if (end < 0 || !int.TryParse(s[(i + 1)..end], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return null;
                steps.Add(new JsonIndex(index));
                i = end + 1;
            }
            else
            {
                return null;
            }
        }

        return new JsonPath(strict, [.. steps]);
    }

    /// <summary>
    /// What a path finds in a JSON value: its shape when present and not null (null when it is never there, or always JSON null),
    /// whether it may be missing, and whether it may be JSON null. Null when the path goes where the shape says nothing (a
    /// record's keys, an array's items by index, a recursive node), which is not read.
    /// </summary>
    private sealed record Found(Shape? Shape, bool MayBeMissing, bool MayBeNull);

    private static Found? Follow(Shape shape, EquatableArray<JsonStep> steps, int from = 0)
    {
        if (from == steps.Count)
            return new Found(shape, false, false);
        switch (shape, steps[from])
        {
            case (UnionShape union, _):
                Found? all = null;
                foreach (var variant in union.Variants)
                {
                    if (Follow(variant, steps, from) is not { } one)
                        return null;
                    all = all is null ? one : Either(all, one);
                }

                return all;
            case (ObjectShape obj, JsonKey(var key)):
                if (obj.Fields.FirstOrDefault(f => f.Name == key) is not { } field)
                    return new Found(null, true, false);
                var nullable = field.NullableBecause.Count > 0;
                var missing = field.Presence == IrPresence.OmittedWhenNull && nullable;
                if (Follow(field.Type, steps, from + 1) is not { } below)
                    return null;
                return below with { MayBeMissing = below.MayBeMissing || missing || (nullable && from + 1 < steps.Count), MayBeNull = below.MayBeNull || (nullable && !missing && from + 1 == steps.Count) };
            case (TupleShape tuple, JsonIndex(var index)):
                if (index >= tuple.Items.Count)
                    return new Found(null, true, false);
                var item = tuple.Items[index];
                if (Follow(item.Type, steps, from + 1) is not { } inner)
                    return null;
                var itemNull = item.NullableBecause.Count > 0;
                return inner with { MayBeMissing = inner.MayBeMissing || (itemNull && from + 1 < steps.Count), MayBeNull = inner.MayBeNull || (itemNull && from + 1 == steps.Count) };
            case (ObjectShape or TupleShape or ArrayShape or ScalarShape or JsonTextShape, _):
                // A key of an array or a scalar, an index of an object or a scalar: not there.
                return shape is ArrayShape && steps[from] is JsonIndex ? null : new Found(null, true, false);
            default:
                return null;
        }

        static Found Either(Found a, Found b) => new(
            a.Shape is null ? b.Shape : b.Shape is null ? a.Shape : JsonShapes.Join(a.Shape, b.Shape),
            a.MayBeMissing || b.MayBeMissing,
            a.MayBeNull || b.MayBeNull);
    }

    /// <summary>
    /// The values a document's rows are, for OPENJSON: an array's items (each may be JSON null when its items may be), an
    /// object itself. Null when not known.
    /// </summary>
    private static List<(Shape Shape, bool MayBeNull)>? Rows(Shape document)
    {
        switch (document)
        {
            case ObjectShape or RecordShape:
                return [(document, false)];
            case TupleShape tuple:
                return [.. tuple.Items.Select(i => (i.Type, i.NullableBecause.Count > 0))];
            case ArrayShape array:
                return [(array.Items, array.ItemsNullableBecause is { Count: > 0 })];
            case UnionShape union:
                var all = new List<(Shape, bool)>();
                foreach (var variant in union.Variants)
                {
                    if (Rows(variant) is not { } rows)
                        return null;
                    all.AddRange(rows);
                }

                return all;
            default:
                return null;
        }
    }

    // ---- conversions --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The types OPENJSON's WITH was observed to give back unchanged from the text FOR JSON writes of them (see
    /// <see cref="Converted"/>): not float or real, whose text may round, nor the date and time types not observed.
    /// </summary>
    private static readonly BuiltInType[] Exact =
    [
        BuiltInType.TinyInt, BuiltInType.SmallInt, BuiltInType.Int, BuiltInType.BigInt, BuiltInType.Decimal, BuiltInType.Numeric,
        BuiltInType.Bit, BuiltInType.Date, BuiltInType.DateTime2, BuiltInType.DateTimeOffset, BuiltInType.UniqueIdentifier,
        BuiltInType.Money, BuiltInType.SmallMoney, BuiltInType.Char, BuiltInType.NChar, BuiltInType.VarChar, BuiltInType.NVarChar,
    ];

    /// <summary>
    /// A JSON scalar of <paramref name="source"/> converted to <paramref name="target"/>, as OPENJSON's WITH converts it: the value
    /// it gives, and whether that is never NULL for a non-null JSON value; null when the conversion is not one known never to
    /// fail or give NULL. Observed (Openjson_with_over_json_built_here_converts_its_own_types_exactly_and_cuts_strings_short):
    /// each type back into itself gives the value unchanged, so its facts and value set hold (an identity conversion, for
    /// integers, decimals, strings, bit, date, datetime2, datetimeoffset, uniqueidentifier and money; not float, whose text
    /// FOR JSON writes may round); a string into any string type is never NULL, cut short past its length, so only that type's
    /// facts hold unless it fits; an integer into a wider integer is the same number. Anything else (a number into a
    /// narrower type, which overflows: error 244, or NULL under the caller's options) is not read.
    /// </summary>
    private Value? Converted(ScalarShape source, SqlType target, SourceLocation at)
    {
        if (Unpadded(SqlTypes.ToConvertedShape(model, target, at)) is not ScalarShape { BuiltIn: { } into } toShape)
            return null;
        var (sourceType, from, to) = (source.Type, source.BuiltIn, toShape.Type);
        if (from is not null && from == into && from.Type.IsAnyOf(Exact))
        {
            // A varchar or char value converted to the database's code page and back may change a character that is not ASCII
            // (observed: Openjson_with_converts_a_varchar_to_the_databases_code_page), so its set holds only when every value is
            // ASCII. Which code page the source column has is not followed.
            var codePage = from.Type.IsAnyOf(BuiltInType.VarChar, BuiltInType.Char) && sourceType.Facets?.Values is { } items && items.Items.Any(v => v.Text.Any(c => c > '\u007f'));
            return Value.Computed(codePage ? source.WithFacets(sourceType.Facets! with { Values = null }) : source, []);
        }

        // A string into a string: never NULL, cut short past the target's length (observed). Its facts hold when every value fits.
        if (sourceType.Kind == IrScalarKind.String && into.IsString && (from is null ? sourceType.Format is null : from.IsString))
        {
            var longest = from is { IsMax: false } ? from.Length
                : sourceType.Facets?.Values is { } set && set.Items.All(v => v.IsString) ? set.Items.Max(v => v.Text.Length)
                : (int?)null;
            var ascii = sourceType.Facets?.Values is { } values && values.Items.All(v => v.Text.All(c => c <= '\u007f'));
            var fits = into.IsMax || (longest is { } n && n <= into.Length);
            var keeps = fits && (into.Type.IsAnyOf(BuiltInType.NVarChar) || (into.Type.IsAnyOf(BuiltInType.VarChar) && from is null && ascii));
            return Value.Computed(keeps ? toShape.WithFacets(Keep(to.Facets, sourceType.Facets)) : toShape, []);
        }

        // An integer into a wider integer, or a JSON literal's integers into an integer type that holds them: the same numbers.
        if (into.IsInteger && from is { IsInteger: true } && from.IntegerDigits <= into.IntegerDigits)
            return Value.Computed(toShape, []);
        if (into.IsInteger && from is null && sourceType.Kind == IrScalarKind.Number && sourceType.Facets?.Values is { } numbers
            && numbers.Items.All(v => ExactDecimal.TryParse(v.Text, out var d) && d.IsInteger && Within(d, into)))
        {
            return Value.Computed(toShape.WithFacets((to.Facets ?? new IrFacets()) with { Values = numbers }), []);
        }

        return null;

        static bool Within(ExactDecimal d, ScalarType type) => d >= type.Type.Least!.Value && d <= type.Type.Greatest!.Value;

        // A string's facts that hold of the same values in the target type: its length bound and its set.
        static IrFacets? Keep(IrFacets? type, IrFacets? source) =>
            source is null ? type : (type ?? new IrFacets()) with { MinLength = source.MinLength, Values = source.Values };
    }

    /// <summary>
    /// The type an OPENJSON WITH column of <paramref name="declared"/> gives: an <c>nchar(n)</c> is not padded, so it is at most n,
    /// not exactly n, where a <c>char(n)</c> is (observed: Openjson_with_does_not_pad_nchar_but_pads_char). A
    /// value that is already n long, as an <c>nchar(n)</c> column through FOR JSON is, keeps its fixed length through the
    /// identity conversion (<see cref="Converted"/>).
    /// </summary>
    internal static Shape Unpadded(Shape declared) =>
        declared is ScalarShape { Type.Facets: { Length: { IsFixed: true } length } facets, BuiltIn.Type: var type } scalar && type == BuiltInType.NChar
            ? scalar.WithFacets(facets with { Length = length with { IsFixed = false } })
            : declared;

    // ---- OPENJSON -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The columns OPENJSON gives over a document built here, or null when its document is not (the general rule then holds).
    /// Without WITH, <c>key</c> and <c>type</c> take value sets where the shape fixes them, and <c>value</c> is never null when no
    /// row's value can be JSON null. With WITH, a column is never null where its path finds a value in every row that is never
    /// JSON null and converts without failing (<see cref="Converted"/>); under a <c>strict</c> path a missing value is an error,
    /// not a NULL (observed). An AS JSON column holds the object or array there, live, and NULL for anything else.
    /// </summary>
    private List<Value>? TracedOpenJson(OpenJsonTableReference reference, ReadScope scope, SourceLocation at)
    {
        if (DocumentOf(reference.Variable, scope) is not ({ } document, _))
            return null;
        if (reference.RowPattern is { } pattern)
        {
            if (pattern is not StringLiteral { Value: var text } || ParsePath(text) is not { } root || Follow(document, root.Steps) is not { Shape: { } inner } found || found.MayBeMissing || found.MayBeNull)
                return null;
            document = inner;
        }

        if (Rows(document) is not { } rows)
            return null;
        static IrNullReason JsonValue(SourceLocation where) => new(IrNullRule.JsonNullOrAbsent, where.ToString());

        if (reference.SchemaDeclarationItems.Count == 0)
        {
            var key = (ScalarShape)SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, 4000), at);
            var type = (ScalarShape)SqlTypes.ToConvertedShape(ScalarType.Of(BuiltInType.TinyInt), at);
            var from = new IrFactSource(IrFactSourceKind.Engine, "OPENJSON over JSON built here");
            var keys = Keys(document);
            var types = Types(document);
            var mayBeNull = types is null || types.Contains(0);
            return
            [
                Value.Computed(keys is null ? key : key with { Type = key.Type with { Facets = WithValues(key.Type.Facets, new IrValueSet([.. keys.Select(k => new IrValue(k, IsString: true))], IrValueMatch.Exact, [from])) } }, []),
                Value.Computed(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, -1), at), mayBeNull ? [JsonValue(at)] : []),
                Value.Computed(types is null ? type : type with { Type = type.Type with { Facets = WithValues(type.Type.Facets, new IrValueSet([.. types.Order().Select(n => new IrValue(n.ToString(CultureInfo.InvariantCulture), IsString: false))], IrValueMatch.Exact, [from])) } }, []),
            ];
        }

        var values = new List<Value>();
        foreach (var item in reference.SchemaDeclarationItems)
        {
            var column = item.ColumnDefinition;
            var where = At(column);
            var path = ParsePath(item.Mapping is StringLiteral mapping ? mapping.Value : $"$.\"{column.ColumnIdentifier.Value}\"");
            Found? found = null;
            foreach (var (row, rowNull) in rows)
            {
                var one = path is null ? null : rowNull ? new Found(null, true, false) : Follow(row, path.Steps);
                if (one is null)
                {
                    found = null;
                    break;
                }

                found = found is null ? one : new Found(
                    found.Shape is null ? one.Shape : one.Shape is null ? found.Shape : JsonShapes.Join(found.Shape, one.Shape),
                    found.MayBeMissing || one.MayBeMissing,
                    found.MayBeNull || one.MayBeNull);
            }

            // Every row has it, not null, or a strict path fails where it is missing.
            var present = found is { Shape: not null, MayBeNull: false } && (!found.MayBeMissing || path!.Strict);
            if (item.AsJson)
            {
                values.Add(present && found!.Shape is ObjectShape or TupleShape or ArrayShape or RecordShape
                    ? new Value(found.Shape, [], IsLiveJson: true)
                    : found?.Shape is { } some && Openable(some)
                        ? new Value(some, [JsonValue(where)], IsLiveJson: true)
                        : new Value(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{column.ColumnIdentifier.Value} is JSON read from a document whose shape there is not known.", where), [JsonValue(where)], IsLiveJson: true));
                continue;
            }

            var declared = SyntaxReader.Type(column.DataType);
            if (declared is not null && found?.Shape is ScalarShape scalar && Converted(scalar, declared, where) is { } converted)
                values.Add(present ? converted : converted.WithNullableBecause([JsonValue(where)]));
            else
                values.Add(Value.Computed(Unpadded(SqlTypes.ToConvertedShape(model, declared, where)), [JsonValue(where)]));
        }

        return values;

        // An object or array, or a union of them: what AS JSON gives when it gives anything.
        static bool Openable(Shape shape) => shape is ObjectShape or TupleShape or ArrayShape or RecordShape
            || (shape is UnionShape u && u.Variants.All(Openable));
    }

    /// <summary>The keys OPENJSON gives over a document: an object's names, an array's positions while bounded; null otherwise.</summary>
    private static IReadOnlyList<string>? Keys(Shape document) => document switch
    {
        ObjectShape obj => [.. obj.Fields.Select(f => f.Name).Order(StringComparer.Ordinal)],
        TupleShape tuple => [.. Enumerable.Range(0, tuple.Items.Count).Select(i => i.ToString(CultureInfo.InvariantCulture))],
        ArrayShape { MaxItems: { } most } when most <= 100 => [.. Enumerable.Range(0, most).Select(i => i.ToString(CultureInfo.InvariantCulture))],
        UnionShape union when union.Variants.Select(Keys).ToList() is var all && all.All(k => k is not null) =>
            [.. all.SelectMany(k => k!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
        _ => null,
    };

    /// <summary>
    /// The types OPENJSON gives over a document's values (0 null, 1 string, 2 number, 3 true or false, 4 array, 5 object): a
    /// key left out when null is never 0; null when not known.
    /// </summary>
    private static HashSet<int>? Types(Shape document)
    {
        var all = new HashSet<int>();
        switch (document)
        {
            case ObjectShape obj:
                foreach (var field in obj.Fields)
                {
                    if (TypeOf(field.Type) is not { } t)
                        return null;
                    all.UnionWith(t);
                    if (field.NullableBecause.Count > 0 && field.Presence == IrPresence.Always)
                        all.Add(0);
                }

                return all;
            case TupleShape tuple:
                foreach (var item in tuple.Items)
                {
                    if (TypeOf(item.Type) is not { } t)
                        return null;
                    all.UnionWith(t);
                    if (item.NullableBecause.Count > 0)
                        all.Add(0);
                }

                return all;
            case ArrayShape array:
                if (TypeOf(array.Items) is not { } items)
                    return null;
                all.UnionWith(items);
                if (array.ItemsNullableBecause is { Count: > 0 })
                    all.Add(0);
                return all;
            case UnionShape union:
                foreach (var variant in union.Variants)
                {
                    if (Types(variant) is not { } t)
                        return null;
                    all.UnionWith(t);
                }

                return all;
            default:
                return null;
        }

        static HashSet<int>? TypeOf(Shape shape) => shape switch
        {
            ScalarShape { Type.Kind: IrScalarKind.Number } => [2],
            ScalarShape { Type.Kind: IrScalarKind.Boolean } => [3],
            ScalarShape or JsonTextShape => [1],
            ArrayShape or TupleShape => [4],
            ObjectShape or RecordShape => [5],
            UnionShape u when u.Variants.Select(TypeOf).ToList() is var each && each.All(t => t is not null) => [.. each.SelectMany(t => t!)],
            _ => null,
        };
    }

    // ---- JSON_VALUE ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>JSON_VALUE(doc, path)</c> over a document built here: nvarchar(4000), never null where the path finds a value in the
    /// document that is never JSON null, is a scalar, and is at most 4,000 characters (or a strict path, which makes anything
    /// else an error); a string keeps its length bound and value set, and true or false is <c>'true'</c> or <c>'false'</c>
    /// (observed: Json_value_over_json_built_here_gives_its_scalars_as_text). Null when the document or path is not read.
    /// </summary>
    private Value? TracedJsonValue(FunctionCall call, ReadScope scope)
    {
        if (call.Parameters.Count != 2 || LiteralOf(call.Parameters[1]) is not ({ } literal and StringLiteral, false) || ParsePath(literal.Value) is not { } path)
            return null;
        if (DocumentOf(call.Parameters[0], scope) is not ({ } document, var documentNull) || Follow(document, path.Steps) is not { } found)
            return null;
        var at = At(call);
        var type = (ScalarShape)SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, 4000), at);

        // A NULL document gives NULL.
        var nullable = NullReasons.Join(documentNull, [new IrNullReason(IrNullRule.JsonNullOrAbsent, at.ToString())]);
        if (found.Shape is not ScalarShape { Type: var scalar } foundScalar)
            return Value.Computed(type, nullable);

        var facets = type.Type.Facets;
        var fits = true;
        switch (scalar.Kind)
        {
            case IrScalarKind.String when scalar.Format is null:
                var longest = foundScalar.BuiltIn is { IsMax: false } t ? t.Length
                    : scalar.Facets?.Values is { } set && set.Items.All(v => v.IsString) ? set.Items.Max(v => v.Text.Length)
                    : (int?)null;
                fits = longest is { } n && n <= 4000;
                if (fits)
                    facets = (facets ?? new IrFacets()) with { MinLength = scalar.Facets?.MinLength, Values = scalar.Facets?.Values };
                break;
            case IrScalarKind.Boolean:
                facets = (facets ?? new IrFacets()) with
                {
                    Values = new IrValueSet([new IrValue("false", IsString: true), new IrValue("true", IsString: true)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Engine, "JSON_VALUE of true or false")]),
                };
                break;
        }

        var present = !found.MayBeNull && (!found.MayBeMissing || path.Strict) && (fits || path.Strict);
        return Value.Computed(type.WithFacets(facets), present ? documentNull : nullable);
    }
}
