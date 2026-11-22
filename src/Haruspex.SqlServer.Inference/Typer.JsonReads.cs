using System.Collections.Immutable;
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

// OPENJSON, JSON_VALUE and json_query with a path over JSON whose shape is known, built here or declared for a column: they read
// a document whose shape is known, so what they give at a path is known too. Each rule was observed on SQL Server 2025 RTM-CU10
// (JsonTracingObservationTests, DeclaredColumnObservationTests). See README.md, "JSON built here, traced" and "Paths into JSON".
internal sealed partial class Typer
{
    /// <summary>
    /// The JSON a document argument holds when its shape is known (built here, or declared for a column): live JSON its shape, JSON
    /// text its content, a literal that is a JSON document its parsed shape; and why it may be NULL. Null when not known.
    /// </summary>
    private (Shape Shape, EquatableArray<IrNullReason> NullableBecause)? DocumentOf(ScalarExpression document, ReadScope scope)
    {
        if (LiteralOf(document) is ({ } literal and StringLiteral, false))
            return JsonLiterals.Parse(literal.Value, literal is StringLiteral { IsNational: true }, At(literal)) is var parsed and not UnknownShape ? (parsed, []) : null;
        var value = Expression(document, scope);
        var shape = value.IsLiveJson ? value.Shape : value.Shape is JsonTextShape { Content: var content } ? content : Assembled(value, document);
        return shape is null or UnknownShape or RecursiveShape ? null : (shape, value.NullableBecause);
    }

    // ---- paths --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// What a literal path finds in <paramref name="document"/> (<see cref="JsonPathWalk"/>), warning when the shape says it can
    /// never find anything there; null when the path is not a literal that is read, or what it finds is not known.
    /// </summary>
    private (JsonPath Path, JsonPathWalk.Found Found)? Walk(Shape document, ScalarExpression pathArgument, TSqlFragment call)
    {
        if (LiteralOf(pathArgument) is not ({ } literal and StringLiteral, false) || JsonPath.Parse(literal.Value, out _) is not { } path
            || JsonPathWalk.Follow(document, path) is not { } found)
        {
            return null;
        }

        if (found.NeverThere)
        {
            summaries.Warn(InferenceCodes.PathFindsNothing.At(
                At(call),
                $"{Text(call)} reads `{path.Prefix(path.Steps.Count)}`, which the JSON there never has ({string.Join("; ", found.Notes)}), so it is always NULL under a lax path and an error under a strict one. Check the path against the JSON's shape or schema."));
        }

        return (path, found);
    }

    /// <summary>
    /// Why a value read from JSON at a path may be NULL: <c>json-null-or-absent</c> at <paramref name="at"/>, with the steps that
    /// make it so.
    /// </summary>
    private static IrNullReason PathNull(SourceLocation at, JsonPathWalk.Found found) =>
        new(IrNullRule.JsonNullOrAbsent, at.ToString()) { Detail = Detail(found) };

    /// <summary>
    /// The steps of a path that may find nothing or null, each named by the path as far as it, for <c>explain</c>: <c>`$.address`
    /// may be left out, as the schema doesn't require it</c>. Null when none is known.
    /// </summary>
    private static string? Detail(JsonPathWalk.Found found) => found.Notes.Count == 0 ? null : string.Join("; ", found.Notes);

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
    private static readonly ImmutableArray<BuiltInType> Exact =
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
        if (from is not null && from == into && from.Type.IsAnyOf(Exact.AsSpan()))
        {
            // A varchar or char value converted to the database's code page and back may change a character that is not ASCII
            // (observed: Openjson_with_converts_a_varchar_to_the_databases_code_page), so its set holds only when every value is
            // ASCII. Which code page the source column has is not followed.
            var codePage = from.Type.IsAnyOf(BuiltInType.VarChar, BuiltInType.Char) && sourceType.Facets?.Values is { } items && items.Items.Any(v => v.Text.Any(c => c > '\u007f'));
            return Value.Computed(codePage && sourceType.Facets is { } facets ? source.WithFacets(facets with { Values = null }) : source, []);
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

        static bool Within(ExactDecimal d, ScalarType type) => type.Type.Least is { } least && type.Type.Greatest is { } greatest && d >= least && d <= greatest;

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

    /// <summary>
    /// An OPENJSON WITH column of <paramref name="declared"/> where nothing more is known of what its path finds: of that type,
    /// NULL for a JSON null or nothing there (<c>json-null-or-absent</c>), and into a decimal NULL too for a number or string of
    /// more places than the type keeps, which WITH rounds as CAST does (observed:
    /// A_loss_of_precision_is_null_when_numeric_roundabort_is_on_and_ansi_warnings_and_arithabort_are_off, "a JSON number read
    /// by OPENJSON into a decimal"): a document's numbers may have any number of places. Into money it rounds unseen.
    /// </summary>
    private Value OpenJsonColumn(SqlType? declared, SourceLocation where, IrNullReason? why = null)
    {
        var shape = Unpadded(SqlTypes.ToConvertedShape(model, declared, where));
        EquatableArray<IrNullReason> because =
        [
            why ?? new IrNullReason(IrNullRule.JsonNullOrAbsent, where.ToString()),
            .. shape is ScalarShape { BuiltIn.IsDecimal: true } ? PrecisionLoss(summaries.Session, where) : [],
        ];
        return Value.Computed(shape, because);
    }

    // ---- OPENJSON -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The columns OPENJSON gives over a document of known shape, built here or declared, or null when it is not known (the general
    /// rule then holds). A row path finds the object or array to open; anything else there gives no rows under a lax path and an
    /// error under a strict one (observed: Openjson_opens_only_an_object_or_array_at_its_path), so only its objects and arrays
    /// are rows. Without WITH, <c>key</c> and <c>type</c> take value sets where the shape fixes them, and <c>value</c> is never null
    /// when no row's value can be JSON null. With WITH, see <see cref="WithColumn"/>.
    /// </summary>
    private List<Value>? TracedOpenJson(OpenJsonTableReference reference, ReadScope scope, SourceLocation at)
    {
        if (DocumentOf(reference.Variable, scope) is not ({ } document, _))
            return null;
        if (reference.RowPattern is { } pattern)
        {
            if (Walk(document, pattern, reference) is not (_, { Shape: { } inner }) || ObjectsAndArrays(inner) is not { } opened)
                return null;
            document = opened;
        }

        if (Rows(document) is not { } rows)
            return null;

        if (reference.SchemaDeclarationItems.Count == 0)
        {
            var key = (ScalarShape)SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, 4000), at);
            var type = (ScalarShape)SqlTypes.ToConvertedShape(ScalarType.Of(BuiltInType.TinyInt), at);
            var from = new IrFactSource(IrFactSourceKind.Engine, "OPENJSON over JSON whose shape is known");
            var keys = Keys(document);
            var types = Types(document);
            var mayBeNull = types is null || types.Contains(0);
            return
            [
                Value.Computed(keys is null ? key : key with { Type = key.Type with { Facets = WithValues(key.Type.Facets, new IrValueSet([.. keys.Select(k => new IrValue(k, IsString: true))], IrValueMatch.Exact, [from])) } }, []),
                Value.Computed(SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, -1), at), mayBeNull ? [new IrNullReason(IrNullRule.JsonNullOrAbsent, at.ToString())] : []),
                Value.Computed(types is null ? type : type with { Type = type.Type with { Facets = WithValues(type.Type.Facets, new IrValueSet([.. types.Order().Select(n => new IrValue(n.ToString(CultureInfo.InvariantCulture), IsString: false))], IrValueMatch.Exact, [from])) } }, []),
            ];
        }

        return [.. reference.SchemaDeclarationItems.Select(item => WithColumn(item, rows))];
    }

    /// <summary>
    /// An OPENJSON WITH column over rows of known shape: what its path finds in every row, joined
    /// (<see cref="JsonPathWalk.Found"/>), then converted to its declared type. JSON <c>null</c> there is NULL under either mode;
    /// nothing there, or a value of the wrong kind, is NULL under a lax path and an error under a strict one (observed:
    /// Openjson_with_under_a_strict_path_errs_where_nothing_is_there_but_gives_null_for_null). A scalar column is typed by its
    /// declared SQL type, never by the JSON's: see <see cref="ScalarColumn"/>.
    /// </summary>
    private Value WithColumn(SchemaDeclarationItemOpenjson item, List<(Shape Shape, bool MayBeNull)> rows)
    {
        var column = item.ColumnDefinition;
        var where = At(column);
        var text = item.Mapping is StringLiteral mapping ? mapping.Value : $"$.\"{column.ColumnIdentifier.Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        var path = JsonPath.Parse(text, out _);
        JsonPathWalk.Found? found = null;
        foreach (var (row, rowNull) in rows)
        {
            var one = path is null ? null : rowNull ? new JsonPathWalk.Found(null, true, false, ["a row may be JSON null, which has nothing below it"]) : JsonPathWalk.Follow(row, path);
            if (one is null)
            {
                found = null;
                break;
            }

            found = found is null ? one : JsonPathWalk.Either(found, one);
        }

        IrNullReason Because() => path is not null && found is not null ? PathNull(where, found) : new IrNullReason(IrNullRule.JsonNullOrAbsent, where.ToString());
        if (item.AsJson)
        {
            // Only an object or an array is given; under a strict path anything else is an error.
            if (path is null || found?.Shape is not { } some || ObjectsAndArrays(some) is not { } opened)
                return new Value(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{column.ColumnIdentifier.Value} is JSON read from a document whose shape there is not known.", where), [Because()], IsLiveJson: true);
            var given = !found.MayBeNull && (path.Strict || (!found.MayBeMissing && found.Variants.All(IsObjectOrArray)));
            return new Value(opened, given ? [] : [Because()], IsLiveJson: true);
        }

        return ScalarColumn(SyntaxReader.Type(column.DataType), found, path, where, Because);
    }

    /// <summary>
    /// A scalar OPENJSON WITH column of <paramref name="declared"/> over what its path finds. Never NULL where every row has a
    /// scalar there that is never JSON null (or the path is strict, so that nothing there, or an object or array, is an error),
    /// and its conversion to the
    /// declared type either keeps the value (<see cref="Converted"/>, which also keeps its facts) or fails with an error rather
    /// than NULL (<see cref="ConversionNull"/>). A conversion that can fail is a run-time error, not a type.
    /// </summary>
    private Value ScalarColumn(SqlType? declared, JsonPathWalk.Found? found, JsonPath? path, SourceLocation where, Func<IrNullReason> because)
    {
        if (declared is null || found is null || path is null)
            return OpenJsonColumn(declared, where);
        var present = found is { Shape: not null, MayBeNull: false } && (path.Strict || (!found.MayBeMissing && found.Variants.All(v => v is ScalarShape)));
        if (found.Shape is ScalarShape scalar && Converted(scalar, declared, where) is { } converted)
            return present ? converted : converted.WithNullableBecause([because()]);
        if (present && SqlTypes.Modelled(SchemaRules.ResolveType(model, declared)?.Scalar) is { } target && ConversionNull(target, where) is { } reasons)
            return Value.Computed(Unpadded(SqlTypes.ToConvertedShape(model, declared, where)), reasons);
        return OpenJsonColumn(declared, where, because());
    }

    /// <summary>
    /// Why a JSON scalar converted to <paramref name="target"/> by OPENJSON's WITH may be NULL, or null when that is not known. A
    /// value that doesn't convert is an error, with two exceptions, which are NULL only when the caller's session has
    /// ANSI_WARNINGS and ARITHABORT off: a number too large for a number type, and a string out of range for datetime or
    /// smalldatetime; a decimal may also be rounded to NULL (observed:
    /// Openjson_with_errs_on_a_value_that_does_not_convert_but_gives_null_for_overflow_under_the_callers_options). A string type
    /// takes anything, cut short.
    /// </summary>
    private EquatableArray<IrNullReason>? ConversionNull(ScalarType target, SourceLocation where) => target switch
    {
        { IsString: true } => [],
        { IsDecimal: true } => NullReasons.Join(Overflow(summaries.Session, where), PrecisionLoss(summaries.Session, where)),
        { IsInteger: true } => Overflow(summaries.Session, where),
        _ when target.Type.IsAnyOf(BuiltInType.Money, BuiltInType.SmallMoney, BuiltInType.Float, BuiltInType.Real, BuiltInType.DateTime, BuiltInType.SmallDateTime) => Overflow(summaries.Session, where),
        _ when target.Type.IsAnyOf(BuiltInType.Bit, BuiltInType.Date, BuiltInType.DateTime2, BuiltInType.DateTimeOffset, BuiltInType.Time, BuiltInType.UniqueIdentifier) => [],
        _ => null,
    };

    /// <summary>Whether a value of <paramref name="shape"/> is always an object or an array: never a scalar.</summary>
    private static bool IsObjectOrArray(Shape shape) =>
        shape is ObjectShape or RecordShape or TupleShape or ArrayShape or AnyJsonShape { ObjectOrArray: true };

    /// <summary>
    /// The objects and arrays among what <paramref name="shape"/> may be, which are what <c>json_query</c>, a row path and an
    /// AS JSON column give: any JSON narrowed to them; null when it is never one.
    /// </summary>
    private static Shape? ObjectsAndArrays(Shape shape) => shape switch
    {
        ObjectShape or RecordShape or TupleShape or ArrayShape => shape,
        AnyJsonShape => new AnyJsonShape(ObjectOrArray: true),
        UnionShape union when union.Variants.Select(ObjectsAndArrays).OfType<Shape>().ToList() is { Count: > 0 } kept =>
            kept.Count == 1 ? kept[0] : kept.Aggregate(JsonShapes.Join),
        _ => null,
    };

    /// <summary>The keys OPENJSON gives over a document: an object's names, an array's positions while bounded; null otherwise.</summary>
    private static IReadOnlyList<string>? Keys(Shape document) => document switch
    {
        // An open object may hold keys it doesn't list, so its keys are not a set.
        ObjectShape { Open: true } => null,
        ObjectShape obj => [.. obj.Fields.Select(f => f.Name).Order(StringComparer.Ordinal)],
        TupleShape tuple => [.. Enumerable.Range(0, tuple.Items.Count).Select(i => i.ToString(CultureInfo.InvariantCulture))],
        ArrayShape { MaxItems: { } most } when most <= 100 => [.. Enumerable.Range(0, most).Select(i => i.ToString(CultureInfo.InvariantCulture))],
        UnionShape union when union.Variants.Select(Keys).ToList() is var all && all.All(k => k is not null) =>
            [.. all.OfType<IReadOnlyList<string>>().SelectMany(k => k).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
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
            case ObjectShape { Open: true }:
                return null;
            case ObjectShape obj:
                foreach (var field in obj.Fields)
                {
                    if (TypeOf(field.Type) is not { } t)
                        return null;
                    all.UnionWith(t);
                    if (field.NullableBecause.Count > 0 && field.Presence != IrPresence.OmittedWhenNull)
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
            UnionShape u when u.Variants.Select(TypeOf).ToList() is var each && each.All(t => t is not null) => [.. each.OfType<HashSet<int>>().SelectMany(t => t)],
            _ => null,
        };
    }

    // ---- JSON_VALUE ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>JSON_VALUE(doc, path)</c> over a document of known shape: nvarchar(4000) (observed for every string type of document and
    /// for json), the text of the scalar the path finds. JSON <c>null</c> there is NULL under either mode; nothing there, an object
    /// or an array, or text longer than 4,000 UTF-16 code units (a string's, or a number's as it is written) is NULL under a lax
    /// path and an error under a strict one (observed: Json_value_gives_null_for_what_it_cannot_return_under_lax_and_errs_under_strict,
    /// Json_value_counts_its_4000_characters_in_utf_16_code_units_after_unescaping). A string keeps its length bound and value set;
    /// true or false is <c>'true'</c> or <c>'false'</c>; a number is its text as written (<c>1.0</c>, <c>1e3</c> and <c>-0</c>
    /// stay as they are), so it keeps no fact (observed: Json_value_of_a_number_is_its_text_as_written). Null when the document or
    /// path is not read.
    /// </summary>
    private Value? TracedJsonValue(FunctionCall call, ReadScope scope)
    {
        if (call.Parameters.Count != 2 || DocumentOf(call.Parameters[0], scope) is not ({ } document, var documentNull)
            || Walk(document, call.Parameters[1], call) is not ({ } path, { } found))
        {
            return null;
        }

        var at = At(call);
        var type = (ScalarShape)SqlTypes.ToConvertedShape(new ScalarType(BuiltInType.NVarChar, 4000), at);
        var texts = found.Variants.Select(TextOf).ToList();

        // NULL for a NULL document, for JSON null, and under lax for anything it cannot give.
        var givesAll = texts.All(t => t is { Fits: true });
        var nullable = found.MayBeNull || (!path.Strict && (found.MayBeMissing || !givesAll));
        var because = nullable ? NullReasons.Join(documentNull, [PathNull(at, Noted(found, givesAll))]) : documentNull;
        var facets = texts is [{ Facets: { } only }] ? (type.Type.Facets ?? new IrFacets()) with { MinLength = only.MinLength, Values = only.Values } : type.Type.Facets;
        return Value.Computed(type.WithFacets(facets), because);

        // A note for a value it cannot give, beside the steps' own.
        static JsonPathWalk.Found Noted(JsonPathWalk.Found found, bool givesAll) =>
            givesAll ? found : found with { Notes = [.. found.Notes, "JSON_VALUE gives NULL for an object, an array or text longer than 4,000 characters there under a lax path"] };
    }

    /// <summary>
    /// What <c>JSON_VALUE</c> gives of a JSON value of <paramref name="shape"/>: whether it is a scalar whose text always fits in
    /// 4,000 UTF-16 code units, and the facts its text keeps; null when it is not a scalar, which it never gives.
    /// </summary>
    private static (bool Fits, IrFacets? Facets)? TextOf(Shape shape)
    {
        switch (shape)
        {
            case ScalarShape { Type: { Kind: IrScalarKind.String, Format: null } scalar } text:
                var longest = text.BuiltIn is { IsMax: false } t ? t.Length
                    : scalar.Facets?.Values is { } set && set.Items.All(v => v.IsString) ? set.Items.Max(v => v.Text.Length)
                    : scalar.Facets?.Length is { IsFixed: false, Unit: IrLengthUnit.CodePoints } codePoints ? 2 * codePoints.Value
                    : (int?)null;
                var fits = longest is { } n && n <= 4000;
                return (fits, fits ? new IrFacets(MinLength: scalar.Facets?.MinLength, Values: scalar.Facets?.Values) : null);
            case ScalarShape { Type.Kind: IrScalarKind.Boolean }:
                return (true, new IrFacets(Values: new IrValueSet([new IrValue("false", IsString: true), new IrValue("true", IsString: true)], IrValueMatch.Exact, [new IrFactSource(IrFactSourceKind.Engine, "JSON_VALUE of true or false")])));
            case ScalarShape { Type.Kind: IrScalarKind.Number } number:
                // FOR JSON writes a SQL number in a few characters, and a JSON literal's numbers are known; a number declared for a
                // column may be written in any number of digits (1.000…), and its text is what JSON_VALUE gives.
                return (number.BuiltIn is not null || (number.Type.Facets?.Values is { } values && values.From.All(f => f.Kind != IrFactSourceKind.Declared)), null);
            case ScalarShape or JsonTextShape:
                // A date, time or GUID as FOR JSON writes it is short; a string holding JSON may be of any length.
                return (shape is ScalarShape { BuiltIn: not null }, null);
            default:
                return null;
        }
    }

    // ---- json_query with a path ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>json_query(doc, path)</c> over a document of known shape: the object or array the path finds, live, as it is there
    /// (observed: Json_query_with_a_path_gives_the_object_or_array_there_and_null_under_lax_for_anything_else). JSON <c>null</c>
    /// there is NULL under either mode; nothing there, or a scalar, is NULL under a lax path and an error under a strict one. A
    /// path that finds only scalars, or nothing, never gives a value, which is an unknown with that reason; so is a path that is
    /// not a literal, or not read, and a document whose shape is not known.
    /// </summary>
    private Value JsonQueryAt(FunctionCall call, ReadScope scope)
    {
        var at = At(call);
        if (LiteralOf(call.Parameters[1]) is not ({ } literal and StringLiteral, false))
            return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{Text(call)} reads JSON at a path that is not a literal, so what it finds is not known.", at));
        if (JsonPath.Parse(literal.Value, out var why) is null)
            return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{Text(call)} reads JSON at a path that is not read: {why}.", at));
        if (DocumentOf(call.Parameters[0], scope) is not ({ } document, var documentNull))
            return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{Text(call)} reads JSON that is not built here or declared, so its shape is unknown.", at));
        if (Walk(document, call.Parameters[1], call) is not ({ } path, { } found))
            return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, $"{Text(call)} reads JSON whose shape at that path is not known.", at));
        if (found.Shape is not { } shape || ObjectsAndArrays(shape) is not { } given)
        {
            return Value.Unknown(Unsupported(IrUnknownReason.JsonShapeUnknown, path.Strict
                ? $"{Text(call)} never finds an object or array, so under its strict path every call is an error."
                : $"{Text(call)} never finds an object or array, so it is always NULL.", at));
        }

        var always = !found.MayBeNull && (path.Strict || (!found.MayBeMissing && found.Variants.All(IsObjectOrArray)));
        var noted = found.Variants.All(IsObjectOrArray) ? found : found with { Notes = [.. found.Notes, "json_query gives NULL for a scalar there under a lax path"] };
        return new Value(given, always ? documentNull : NullReasons.Join(documentNull, [PathNull(at, noted)]), IsLiveJson: true);
    }
}
