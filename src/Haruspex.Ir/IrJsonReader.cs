using System.Globalization;
using System.Text.Json;
using Haruspex.Collections;

namespace Haruspex.Ir;

/// <summary>
/// Reads an IR file into an <see cref="IrDocument"/>: the inverse of <see cref="IrJsonWriter"/>, so that what a consumer of
/// the file gets is what haruspex inferred. Reading what the writer wrote and writing it again gives the same bytes, and the
/// document read equals the one written; the tests hold every fixture and both public corpora to that round trip.
/// </summary>
/// <remarks>
/// <para>
/// The reader is as strict as the format is closed. It refuses, with an <see cref="IrFormatException"/> that names the path
/// in the document: text that is not JSON; a version other than those in <see cref="IrDocument.ReadableVersions"/>; a property missing,
/// of the wrong type, unknown or given twice; a name outside its vocabulary; a negative count; a flag that disagrees with
/// its reasons (<c>nullable</c> without <c>nullableBecause</c>); and a reference that names no type. It supplies no default: a
/// property is either in the file or, where the format lets the writer leave it out, absent with the meaning the format gives
/// that.
/// </para>
/// <para>
/// A version it doesn't read is refused before anything else, rather than read with its differences guessed at: the IR has
/// no stable version yet (0.x), so a file from another version is regenerated, not migrated. <c>docs/internals/10-the-ir-as-a-contract.md</c>
/// argues the choice. It would read another version only if every file of it were a file of the current version that means
/// the same (<see cref="IrDocument.ReadableVersions"/>, which says why none is at present).
/// </para>
/// </remarks>
public static class IrJsonReader
{
    /// <summary>The deepest nesting read, as deep as the writer's own limit, so that every file the writer writes is read.</summary>
    private const int MaxDepth = 1000;

    /// <summary>The document an IR file holds.</summary>
    /// <param name="json">The file's text.</param>
    /// <exception cref="IrFormatException">The text is not an IR document of this version; the message says where and why.</exception>
    public static IrDocument Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException e)
        {
            // The parser counts lines and bytes from 0; a person counts from 1.
            var where = e.LineNumber is { } line && e.BytePositionInLine is { } position
                ? $" at line {(line + 1).ToString(CultureInfo.InvariantCulture)}, byte {(position + 1).ToString(CultureInfo.InvariantCulture)}"
                : "";
            throw new IrFormatException(e.Path ?? "$", $"is not JSON{where}: {e.Message}", e);
        }

        using (parsed)
        {
            var document = Document(IrJsonObject.At(parsed.RootElement, "$"));
            RequireReferencesResolve(parsed.RootElement, "$", document.Types);
            RequireFoldsResolve(parsed.RootElement, "$", document.Folds);
            return document;
        }
    }

    /// <summary>
    /// Every reference names a type in <c>types</c>: a dangling one would leave a reader with a value of no type at all. JSON
    /// Schema can't say this (it can't compare a value with a key elsewhere in the document), so the reader does. A
    /// reference is the one object of the format with a <c>ref</c> property, so the file is searched for them as it is.
    /// </summary>
    private static void RequireReferencesResolve(JsonElement element, string path, IReadOnlyDictionary<string, IrObjectType> types)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var at = path + IrPath.Key(property.Name);
                    // Never null: GetString is null only for a JSON null, and the value is a string.
                    if (property.Name == "ref" && property.Value.ValueKind == JsonValueKind.String && !types.ContainsKey(property.Value.GetString()!))
                        throw new IrFormatException(at, $"is \"{property.Value.GetString()}\", which names no type in $.types.");
                    RequireReferencesResolve(property.Value, at, types);
                }

                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    RequireReferencesResolve(item, path + "[" + i++.ToString(CultureInfo.InvariantCulture) + "]", types);
                break;
        }
    }

    /// <summary>Every value set's fold names a fold of the document: a value set is the object with a <c>match</c>.</summary>
    private static void RequireFoldsResolve(JsonElement element, string path, EquatableMap<string, IrFold> folds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("match", out _) && element.TryGetProperty("fold", out var fold) && fold.ValueKind == JsonValueKind.String && fold.GetString() is { } name && !folds.ContainsKey(name))
                    throw new IrFormatException(path + IrPath.Key("fold"), $"is \"{name}\", which names no fold in $.folds.");
                foreach (var property in element.EnumerateObject())
                    RequireFoldsResolve(property.Value, path + IrPath.Key(property.Name), folds);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    RequireFoldsResolve(item, path + "[" + i++.ToString(CultureInfo.InvariantCulture) + "]", folds);
                break;
        }
    }

    /// <summary>
    /// The version a file says it is (<c>irVersion</c>), read without the rest of it, so that a program can tell a file of another
    /// version from one that is broken before it reads either. Null when the text isn't JSON, its root isn't an object, or it has
    /// no <c>irVersion</c> that is a string.
    /// </summary>
    /// <param name="json">The file's text.</param>
    public static string? VersionOf(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            return parsed.RootElement.ValueKind == JsonValueKind.Object
                && parsed.RootElement.TryGetProperty("irVersion", out var version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IrDocument Document(IrJsonObject json)
    {
        // The version first: a file of another version may differ anywhere, so nothing else in it is read.
        var version = json.Optional("irVersion") is { } found
            ? IrJsonObject.AsString(found, json.PathOf("irVersion"))
            : throw new IrFormatException(json.PathOf("irVersion"), "is missing, so this is not a haruspex IR file.");
        if (!IrDocument.ReadableVersions.Contains(version, StringComparer.Ordinal))
        {
            throw new IrFormatException(
                json.PathOf("irVersion"),
                $"is \"{version}\", and this reader reads IR {string.Join(" and ", IrDocument.ReadableVersions)} only. Run haruspex generate again to write the file in this version.");
        }

        var source = json.Object("source");
        var document = new IrDocument(
            new IrSource(source.String("dialect"))
            {
                Session = Session(source.OptionalObject("session")),
                Package = Package(source),
                References = References(source),
            },
            [.. json.Array("objects").Select(o => Object(IrJsonObject.At(o.Item, o.Path)))],
            Types(json.Object("types")),
            [.. json.Array("unknowns").Select(u => Unknown(IrJsonObject.At(u.Item, u.Path)))])
        {
            Overrides = [.. json.Array("overrides").Select(o => Override(IrJsonObject.At(o.Item, o.Path)))],
            Folds = Folds(json.Object("folds")),
        };
        source.End();
        json.End();
        return document;
    }

    private static IrObject Object(IrJsonObject json)
    {
        var results = json.Required("results");
        var kind = json.Name<IrObjectKind>("kind", IrWireNames.TryParse, "an object's kind");

        // A query runs its text and a procedure its name, so each has exactly what it runs.
        var text = json.OptionalString("text");
        if (kind == IrObjectKind.Query && text is null)
            throw new IrFormatException(json.PathOf("text"), "is missing: a query's object holds the text it runs.");
        if (kind != IrObjectKind.Query && text is not null)
            throw new IrFormatException(json.PathOf("text"), $"is given, but only a query runs text, and this object is a {IrWireNames.Of(kind)}.");
        var obj = new IrObject(
            json.String("id"),
            json.String("name"),
            kind,
            json.String("at"),
            [.. json.Array("parameters").Select(p => Parameter(IrJsonObject.At(p.Item, p.Path)))],

            // Null, not empty, when the results can't be listed: the writer's distinction, kept.
            results.ValueKind == JsonValueKind.Null
                ? null
                : [.. IrJsonObject.Items(results, json.PathOf("results")).Select(r => Result(IrJsonObject.At(r.Item, r.Path)))])
        {
            Text = text,
            ReturnsCode = json.Boolean("returnsCode"),
        };
        SetNames(obj, json);
        json.End();
        return obj;
    }

    /// <summary>
    /// An object's sets are named all or none, and no two alike, since each name is a key of the one object its function
    /// returns (<c>docs/decisions/0070-a-procedure-s-result-sets-are-named-all-or-none.md</c>).
    /// </summary>
    private static void SetNames(IrObject obj, IrJsonObject json)
    {
        if (obj.Results is not { } results || results.All(r => r.Name is null))
            return;
        if (results.Any(r => r.Name is null))
            throw new IrFormatException(json.PathOf("results"), "names some of its sets and not others; an object's sets are named all or none.");
        if (results.GroupBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new IrFormatException(json.PathOf("results"), $"names two sets \"{twice.Key}\"; each set's name is its own.");
    }

    private static IrParameter Parameter(IrJsonObject json)
    {
        var parameter = new IrParameter(json.String("name"), Type(json.Object("type")), json.Boolean("hasDefault"), json.Boolean("output"));
        json.End();
        return parameter;
    }

    private static IrResult Result(IrJsonObject json)
    {
        var result = new IrResult(
            json.Name<IrEncoding>("encoding", IrWireNames.TryParse, "a result's encoding"),
            Type(json.Object("type")),
            Reasons(json, "mayBeAbsent", "absentBecause"))
        {
            Name = SetName(json),
            Cardinality = json.Name<IrCardinality>("cardinality", IrWireNames.TryParse, "a cardinality"),
        };
        json.End();
        return result;
    }

    /// <summary>
    /// A set's name, a key of its function's object, so an identifier and none of <see cref="IrResult.KeysBesideSets"/>; null when
    /// the set is unnamed.
    /// </summary>
    private static string? SetName(IrJsonObject json)
    {
        var name = json.OptionalString("name");
        if (name is not null && !IrIdentifiers.IsIdentifier(name))
            throw new IrFormatException(json.PathOf("name"), $"is \"{name}\", which is not an identifier.");
        if (name is not null && IrResult.KeysBesideSets.Contains(name, StringComparer.Ordinal))
            throw new IrFormatException(json.PathOf("name"), $"is \"{name}\", which can't name a set: the object its function returns already has that key.");
        return name;
    }

    private static Dictionary<string, IrObjectType> Types(IrJsonObject json)
    {
        // A name given twice is refused when the object is read (IrJsonObject), so each name here is one type.
        var types = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
        foreach (var name in json.Names.ToList())
            types.Add(name, ObjectType(json.Object(name)));
        json.End();
        return types;
    }

    private static EquatableMap<string, IrFold> Folds(IrJsonObject json)
    {
        var folds = json.Names.ToList().ToEquatableMap(name => name, name => Fold(json.Object(name)));
        json.End();
        return folds;
    }

    private static IrFold Fold(IrJsonObject json)
    {
        var ignored = json.String("ignored");
        var classes = json.Object("classes");
        var fold = new IrFold(ignored, classes.Names.ToList().ToEquatableMap(name => name, classes.String));
        classes.End();
        json.End();
        return fold;
    }

    private static IrObjectType ObjectType(IrJsonObject json)
    {
        var kind = json.String("kind");
        if (kind != "object")
            throw new IrFormatException(json.PathOf("kind"), $"is \"{kind}\", but a named type is always \"object\".");

        var type = new IrObjectType([.. json.Array("fields").Select(f => Field(IrJsonObject.At(f.Item, f.Path)))])
        {
            // Left out when false: a type that is not recursive says nothing.
            Recursive = json.OptionalBoolean("recursive") ?? false,
            NameDeclared = json.OptionalBoolean("nameDeclared") ?? false,
            Identity = json.OptionalObject("identity") is { } identity ? Identity(identity) : null,
        };
        json.End();
        return type;
    }

    private static IrTypeIdentity Identity(IrJsonObject json)
    {
        // The owner's kind is the key, and its value the owner: { "procedure": "sales.get_order", "path": "results[0][]" }.
        var owners = json.Names.Where(n => n != "path").ToList();
        if (owners.Count != 1)
            throw new IrFormatException(json.Path, "must name one owner, keyed by its kind, beside its path.");
        if (!IrWireNames.TryParse(owners[0], out IrTypeOwner owner))
            throw new IrFormatException(json.PathOf(owners[0]), $"is not a kind of object a type belongs to in IR {IrDocument.CurrentVersion}.");

        var identity = new IrTypeIdentity(owner, json.String(owners[0]), json.String("path"));
        json.End();
        return identity;
    }

    private static IrField Field(IrJsonObject json)
    {
        var field = new IrField(
            json.String("name"),
            Type(json.Object("type")),
            json.Name<IrPresence>("presence", IrWireNames.TryParse, "a presence"),
            Reasons(json, "nullable", "nullableBecause"));
        json.End();
        return field;
    }

    /// <summary>
    /// The reasons a flag gives: the flag and its list agree, true with at least one reason, false with none and the list
    /// left out. A flag the writer leaves out when false (<paramref name="flagOptional"/>) reads as false.
    /// </summary>
    private static EquatableArray<IrNullReason> Reasons(IrJsonObject json, string flag, string list, bool flagOptional = false)
    {
        var set = flagOptional ? json.OptionalBoolean(flag) ?? false : json.Boolean(flag);
        if (!set)
        {
            return json.Has(list)
                ? throw new IrFormatException(json.PathOf(list), $"is given, but {flag} is false: a value that is never null has no reasons.")
                : [];
        }

        if (!json.Has(list))
            throw new IrFormatException(json.PathOf(list), $"is missing, but {flag} is true: every null needs a reason.");
        EquatableArray<IrNullReason> reasons = [.. json.Array(list).Select(r => Reason(IrJsonObject.At(r.Item, r.Path)))];
        return reasons.Count > 0 ? reasons : throw new IrFormatException(json.PathOf(list), $"is empty, but {flag} is true: every null needs a reason.");
    }

    private static IrNullReason Reason(IrJsonObject json)
    {
        var reason = new IrNullReason(json.Name<IrNullRule>("rule", IrWireNames.TryParse, "a null rule"), json.String("at"));
        json.End();
        return reason;
    }

    private static IrType Type(IrJsonObject json)
    {
        // A reference is the one node without a kind.
        if (json.Has("ref"))
        {
            var reference = new IrRef(json.String("ref"));
            json.End();
            return reference;
        }

        var kind = json.String("kind");
        IrType type = kind switch
        {
            _ when IrWireNames.TryParse(kind, out IrScalarKind scalar) => Scalar(json, scalar),
            "array" => Array(json),
            "tuple" => new IrTuple([.. json.Array("items").Select(e => Element(IrJsonObject.At(e.Item, e.Path)))]),
            "record" => new IrRecord(Type(json.Object("values")), Reasons(json, "valuesNullable", "valuesNullableBecause")),
            "union" => Union(json),
            "json-text" => new IrJsonText(json.OptionalObject("content") is { } content ? Type(content) : null),
            "import" => new IrImport(json.String("name"), json.String("from")),
            "unknown" => new IrUnknownType(json.Name<IrUnknownReason>("reason", IrWireNames.TryParse, "an unknown's reason")),
            _ => throw new IrFormatException(json.PathOf("kind"), $"is \"{kind}\", which is not a type in IR {IrDocument.CurrentVersion}."),
        };
        json.End();
        return type;
    }

    private static IrUnion Union(IrJsonObject json)
    {
        EquatableArray<IrType> variants = [.. json.Array("variants").Select(v => Type(IrJsonObject.At(v.Item, v.Path)))];
        return variants.Count >= 2
            ? new IrUnion(variants)
            : throw new IrFormatException(json.PathOf("variants"), "must have at least two variants: a value of one type is that type, not a union.");
    }

    private static IrArray Array(IrJsonObject json) => new(Type(json.Object("items")))
    {
        // Left out when no item is ever null, as FOR JSON's arrays are.
        ItemsNullableBecause = Reasons(json, "itemsNullable", "itemsNullableBecause", flagOptional: true),
        MinItems = json.OptionalCount("minItems"),
        MaxItems = json.OptionalCount("maxItems"),
    };

    private static IrElement Element(IrJsonObject json)
    {
        var element = new IrElement(Type(json.Object("type")), Reasons(json, "nullable", "nullableBecause"));
        json.End();
        return element;
    }

    private static IrScalar Scalar(IrJsonObject json, IrScalarKind kind) => new(
        kind,
        json.OptionalName<IrFormat>("format", IrWireNames.TryParse, "a scalar's format"),
        json.OptionalString("sql"),
        Facets(json));

    /// <summary>The facets, which the writer puts beside the scalar's kind rather than in an object of their own.</summary>
    private static IrFacets Facets(IrJsonObject json)
    {
        if (json.Has("length") && json.Has("maxLength"))
            throw new IrFormatException(json.Path, "has both length and maxLength: a string's length is fixed or a maximum, not both.");
        var length = json.OptionalObject("length") is { } fixedLength
            ? Length(fixedLength, isFixed: true)
            : json.OptionalObject("maxLength") is { } maxLength ? Length(maxLength, isFixed: false) : null;
        return new IrFacets(
            length,
            json.OptionalObject("minLength") is { } minLength ? MinLength(minLength) : null,
            json.OptionalObject("digits") is { } digits ? Digits(digits) : null,
            json.OptionalObject("min") is { } min ? Bound(min) : null,
            json.OptionalObject("max") is { } max ? Bound(max) : null,
            json.OptionalObject("values") is { } values ? ValueSet(values) : null);
    }

    private static IrLength Length(IrJsonObject json, bool isFixed)
    {
        var length = new IrLength(
            json.Count("value"),
            isFixed,
            json.Name<IrLengthUnit>("unit", IrWireNames.TryParse, "a length's unit"),

            // Left out when not known.
            json.OptionalName<IrByteEncoding>("encoding", IrWireNames.TryParse, "a byte encoding") ?? IrByteEncoding.Unknown,
            Source(json.Object("from")));
        json.End();
        return length;
    }

    private static IrMinLength MinLength(IrJsonObject json)
    {
        var minLength = new IrMinLength(json.Count("value"), Source(json.Object("from")));
        json.End();
        return minLength;
    }

    private static IrDigits Digits(IrJsonObject json)
    {
        var digits = new IrDigits(json.Count("precision"), json.Count("scale"), Source(json.Object("from")));
        json.End();
        return digits;
    }

    private static IrBound Bound(IrJsonObject json)
    {
        var bound = new IrBound(Number(json.Required("value"), json.PathOf("value")), json.Boolean("inclusive"), Source(json.Object("from")));
        json.End();
        return bound;
    }

    private static IrValueSet ValueSet(IrJsonObject json)
    {
        var set = new IrValueSet(
            // Never null: GetString is null only for a JSON null, and the item is a string.
            [.. json.Array("items").Select(i => i.Item.ValueKind == JsonValueKind.String ? new IrValue(i.Item.GetString()!, IsString: true) : new IrValue(Number(i.Item, i.Path), IsString: false))],
            json.Name<IrValueMatch>("match", IrWireNames.TryParse, "a value set's match"),
            [.. json.Array("from").Select(f => Source(IrJsonObject.At(f.Item, f.Path)))],
            json.OptionalString("fold"));
        if (set.Match == IrValueMatch.Canonical ? set.Fold is null : set.Fold is not null)
            throw new IrFormatException(json.PathOf("fold"), "names the fold of a canonical value set, and is there exactly when the match is canonical.");
        json.End();
        return set;
    }

    /// <summary>A number as its exact decimal text, never through a double, in the plain form the writer writes.</summary>
    private static string Number(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number)
            throw new IrFormatException(path, "must be a number.");
        var text = element.GetRawText();
        return IrJsonWriter.IsPlainDecimal(text) ? text : throw new IrFormatException(path, $"is {text}, which is not a plain decimal number such as -12.5.");
    }

    /// <summary>The package a source was read from, or none when the file leaves <c>package</c> out; its hash comes with it.</summary>
    private static IrPackage? Package(IrJsonObject source)
    {
        if (source.Optional("package") is null)
            return source.Optional("sha256") is null ? null : throw new IrFormatException(source.PathOf("package"), "is missing, though sha256, its hash, is given.");
        return new IrPackage(source.String("package"), source.String("sha256"));
    }

    /// <summary>
    /// The packages the source's package refers to; none when the file leaves <c>references</c> out. An empty array is refused:
    /// the writer leaves it out, so there is one way to say "none".
    /// </summary>
    private static EquatableArray<IrPackageReference> References(IrJsonObject source)
    {
        if (source.Optional("references") is null)
            return [];
        var references = source.Array("references").Select(r => Reference(IrJsonObject.At(r.Item, r.Path))).ToList();
        return references.Count > 0 ? [.. references] : throw new IrFormatException(source.PathOf("references"), "must list at least one package; leave references out when there are none.");
    }

    /// <summary>A package the source's package refers to: its name, its hash when it was found, and its database when another.</summary>
    private static IrPackageReference Reference(IrJsonObject json)
    {
        var reference = new IrPackageReference(
            json.String("package"),
            json.OptionalString("sha256"),
            json.OptionalString("database"));
        json.End();
        return reference;
    }

    /// <summary>
    /// The options the callers' sessions are declared to set, sorted by name; none when the file leaves <c>session</c> out. An
    /// empty object is refused: the writer leaves it out, so there is one way to say "none".
    /// </summary>
    private static EquatableArray<IrSessionOption> Session(IrJsonObject? json)
    {
        if (json is null)
            return [];
        var options = json.Names.Select(name => new IrSessionOption(name, json.Boolean(name))).OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
        if (options.Count == 0)
            throw new IrFormatException(json.Path, "must declare at least one option; leave session out when none is declared.");
        json.End();
        return [.. options];
    }

    /// <summary>Where a fact came from: one property, whose name is the kind of source and whose value is its ref.</summary>
    private static IrFactSource Source(IrJsonObject json)
    {
        var names = json.Names.ToList();
        if (names.Count != 1)
            throw new IrFormatException(json.Path, "must have one property, whose name says where the fact came from.");
        if (!IrWireNames.TryParse(names[0], out IrFactSourceKind kind))
            throw new IrFormatException(json.PathOf(names[0]), $"is not where a fact can come from in IR {IrDocument.CurrentVersion}.");
        var source = new IrFactSource(kind, json.String(names[0]));
        json.End();
        return source;
    }

    private static IrUnknown Unknown(IrJsonObject json)
    {
        var unknown = new IrUnknown(
            json.String("object"),
            json.String("path"),
            json.Name<IrUnknownReason>("reason", IrWireNames.TryParse, "an unknown's reason"),
            json.String("detail"),
            json.String("at"));
        json.End();
        return unknown;
    }

    private static IrOverride Override(IrJsonObject json)
    {
        var claim = new IrOverride(
            json.String("object"),
            json.String("path"),
            json.Name<IrOverrideSetting>("setting", IrWireNames.TryParse, "an override's setting"),
            json.OptionalString("why"))
        {
            Via = json.OptionalString("via"),
        };
        json.End();
        return claim;
    }
}
