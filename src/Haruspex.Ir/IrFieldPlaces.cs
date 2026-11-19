using System.Globalization;

namespace Haruspex.Ir;

/// <summary>
/// Where each field of a document is, as a person names it on the command line: the object, then a path into its results in
/// the config file's syntax (<c>orders[].lines[].qty</c>), a result set by its name (<c>summary.total</c>) or, when the sets are
/// unnamed and there are several, by its place, counting from one (<c>#2.total</c>). What <c>haruspex explain</c> reads and what
/// generated comments tell the reader to run, so that the two name a field the same way
/// (<c>docs/decisions/0079-a-surprise-is-explained-where-it-is-written.md</c>).
/// </summary>
/// <remarks>
/// A field is named from its type's identity (<see cref="IrTypeIdentity"/>), the path the inference found it at, with the result
/// set made a name or a place and its rows left out: <c>results[0][].qty</c> is <c>qty</c> when the object has one set.
/// A type with no identity, or one a function owns, which several objects may reach, is named by its type's name instead.
/// </remarks>
public static class IrFieldPlaces
{
    /// <summary>
    /// The object and path a field of <paramref name="typeName"/> is named by, as <c>explain</c> takes them; the type's name and
    /// the field's key when its type belongs to no one object.
    /// </summary>
    public static (string Target, string Path) Of(IrDocument document, string typeName, IrField field)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(field);
        var type = document.Types[typeName];
        if (type.Identity is { Owner: IrTypeOwner.Procedure or IrTypeOwner.Query } identity
            && document.Objects.FirstOrDefault(o => o.Id == identity.Object) is { } owner
            && Display(owner, identity.Path) is { } path)
        {
            return (owner.Id, Join(path, IrPath.Key(field.Name)));
        }

        return (typeName, IrPath.Render([new IrPathKey(field.Name)]));
    }

    /// <summary>The <c>haruspex explain</c> command that explains a field of <paramref name="typeName"/>.</summary>
    public static string Command(IrDocument document, string typeName, IrField field)
    {
        var (target, path) = Of(document, typeName, field);
        return $"haruspex explain {ShellWord(target)} {ShellWord(path)}";
    }

    /// <summary>
    /// The <c>haruspex explain</c> command for the field at <paramref name="irPath"/> (<c>results[0][].note</c>) of the object
    /// <paramref name="objectId"/> in <paramref name="document"/>, or null when no field of it is there: a change's place, as
    /// <c>haruspex check</c> reports it.
    /// </summary>
    public static string? Command(IrDocument document, string objectId, string irPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Objects.FirstOrDefault(o => o.Id == objectId) is not { } obj || Display(obj, irPath) is not { } path
            || Find(document, obj, path) is not { } place)
        {
            return null;
        }

        return $"haruspex explain {ShellWord(obj.Id)} {ShellWord(place.Path)}";
    }

    /// <summary>The <c>haruspex explain</c> command that explains an object's results.</summary>
    public static string Command(IrObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return $"haruspex explain {ShellWord(obj.Id)}";
    }

    /// <summary>
    /// Every field of every type that belongs to <paramref name="obj"/>, with the path it is named by, in the order the document's
    /// types are written.
    /// </summary>
    public static IReadOnlyList<IrFieldPlace> Fields(IrDocument document, IrObject obj)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(obj);
        var places = new List<IrFieldPlace>();
        foreach (var (name, type) in IrDocumentOrder.Types(document))
        {
            if (type.Identity is not { } identity || identity.Object != obj.Id || Display(obj, identity.Path) is not { } path)
                continue;
            places.AddRange(type.Fields.Select(f => new IrFieldPlace(name, f, Join(path, IrPath.Key(f.Name)))));
        }

        return places;
    }

    /// <summary>
    /// The field <paramref name="path"/> names in <paramref name="obj"/>'s results, or null. <c>[]</c> may be written or left
    /// out, since each names the same field: <c>lines.qty</c> is <c>lines[].qty</c>.
    /// </summary>
    public static IrFieldPlace? Find(IrDocument document, IrObject obj, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var wanted = Loose(path);
        return Fields(document, obj).Where(p => Loose(p.Path) == wanted).Select(p => (IrFieldPlace?)p).FirstOrDefault();
    }

    /// <summary>
    /// The path a person names a place by, from the IR's (<c>results[1][].total</c>): the set by its name or place, and its rows
    /// left out, a rowset's or a JSON array's. Null for a path that is not inside the object's results.
    /// </summary>
    private static string? Display(IrObject obj, string irPath)
    {
        if (!IrPath.TryParse(irPath, out var segments, out _) || segments is not [IrPathKey { Key: "results" }, IrPathIndex { Index: var index }, ..]
            || obj.Results is not { } results || index >= results.Count)
        {
            return null;
        }

        var set = results.Count == 1 ? "" : results[index].Name ?? ("#" + (index + 1).ToString(CultureInfo.InvariantCulture));
        var rest = segments.Skip(2).ToList();
        if (results[index].Type is IrArray && rest is [IrPathItems, ..])
            rest.RemoveAt(0);
        return Join(set, IrPath.Render(rest));
    }

    /// <summary>Two parts of a path, with a dot between where the second starts with a bare key.</summary>
    private static string Join(string first, string second) =>
        first.Length == 0 ? second.TrimStart('.')
        : second.Length == 0 ? first
        : second[0] is '.' or '[' or '<' or '{' ? first + second
        : first + "." + second;

    /// <summary>A path without its <c>[]</c>, which a person may leave out.</summary>
    private static string Loose(string path) => path.Replace("[]", "", StringComparison.Ordinal).TrimStart('.');

    /// <summary>
    /// A word as a shell takes it, so that a command written in a comment or a message runs as written: as it is when it holds
    /// nothing a shell reads, and otherwise in single quotes. A <c>*/</c> inside is split as <c>*'"/"'</c>, the same word to a shell,
    /// so that the command never ends the comment it is written in, nor needs the escape a comment would give it.
    /// </summary>
    public static string ShellWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return word.Length > 0 && word.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '/' or '-' or '@')
            ? word
            : "'" + word.Replace("'", "'\\''", StringComparison.Ordinal).Replace("*/", "*'\"/\"'", StringComparison.Ordinal) + "'";
    }
}
