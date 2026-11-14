using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Collections;

namespace Haruspex.SqlServer.Semantics.Collations;

/// <summary>
/// How a collation compares the strings of a <c>char</c> or <c>varchar</c> column in its code page, as far as a set of plain values
/// needs it: which characters it ignores, and which it holds equal to each of the characters a plain value is made of
/// (<see cref="Targets"/>). Observed from the server, character by character, and kept as data (<c>Collations/*.json</c>, written
/// and checked by <c>CollationFoldGridTests</c>); the fold is the server's equality, not an approximation of it
/// (<c>docs/decisions/0043-a-value-set-is-canonical-by-a-fold-observed-from-the-server.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The fold of a string drops every ignored character, puts each other character that equals a class of targets in place of that
/// class's representative, keeps every other character, and then drops trailing spaces, in that order: a value ending with a
/// space and an ignored character is padded and compared equal. Two strings are equal under the collation exactly when their folds
/// are, for strings of the characters the code page holds: the grid finds every character's class the same beside each target on
/// either side, and <c>CanonicalValueSetObservationTests</c> holds it for every character, and every pair, inserted at every
/// position of each member of a sample.
/// </para>
/// <para>
/// Only code pages are proven. Under the Unicode rules some characters compare by their neighbours: a Japanese repeat mark
/// (U+3031) equals the letter before it, so <c>a</c> and the mark equal <c>aa</c> and <c>z</c> and the mark <c>zz</c>, in every
/// collation listed here (observed). A fold of one character at a time can't be that equality, so a set of an <c>nchar</c> or
/// <c>nvarchar</c> column, or of a <c>_UTF8</c> one, keeps its comment.
/// </para>
/// </remarks>
/// <param name="Collation">The collation.</param>
/// <param name="Kind">How the strings are held; <see cref="StringKind.CodePage"/> for every fold proven.</param>
/// <param name="Ignored">The characters it ignores, in ordinal order.</param>
/// <param name="Classes">
/// The characters equal to each class of targets, in ordinal order, by the class's representative (its least target, ordinally):
/// <c>"AE"</c> for <c>"Ææ"</c> under a case-insensitive collation.
/// </param>
public sealed record CollationFold(string Collation, StringKind Kind, string Ignored, EquatableMap<string, string> Classes)
{
    /// <summary>
    /// The characters a plain value is made of: ASCII letters and digits, space, underscore, hyphen and full stop. A value set
    /// is canonical only when every member is made of these (<see cref="IsPlain"/>).
    /// </summary>
    public const string Targets = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz _-.";

    /// <summary>
    /// The collations proven, each with the kind of string it is proven for: the sets of a column held so are canonical, and
    /// any other's keep their comment. The grid test derives exactly these.
    /// </summary>
    public static IReadOnlyList<(string Collation, StringKind Kind)> Proven { get; } =
    [
        ("SQL_Latin1_General_CP1_CI_AS", StringKind.CodePage),
        ("Latin1_General_100_CI_AS", StringKind.CodePage),
        ("Latin1_General_100_CS_AS", StringKind.CodePage),
        ("Latin1_General_100_CI_AI", StringKind.CodePage),
        ("Latin1_General_CI_AS", StringKind.CodePage),
    ];

    private static readonly FrozenDictionary<(string, StringKind), Lazy<CollationFold>> Loaded = Proven.ToFrozenDictionary(
        p => (p.Collation.ToUpperInvariant(), p.Kind),
        p => new Lazy<CollationFold>(() => Read(p.Collation, p.Kind)));

    /// <summary>The fold of a proven collation and kind, or null for one not proven.</summary>
    public static CollationFold? For(string collation, StringKind kind)
    {
        ArgumentNullException.ThrowIfNull(collation);
        return Loaded.TryGetValue((collation.ToUpperInvariant(), kind), out var fold) ? fold.Value : null;
    }

    /// <summary>The proven fold of a <see cref="Name"/>.</summary>
    /// <exception cref="ArgumentException">No proven fold has the name.</exception>
    public static CollationFold Named(string name) =>
        Proven.FirstOrDefault(p => FileName(p.Collation, p.Kind) == name) is ({ } collation, var kind) && For(collation, kind) is { } fold
            ? fold
            : throw new ArgumentException($"No proven fold is named {name}.", nameof(name));

    /// <summary>The name of its data file and of the fold in the IR: <c>Latin1_General_100_CI_AS.codepage</c>.</summary>
    public string Name => FileName(Collation, Kind);

    /// <summary>Whether every character of <paramref name="value"/> is one a plain value is made of, and it has at least one.</summary>
    public static bool IsPlain(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 0 && value.All(c => Targets.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>The fold of <paramref name="value"/> (see the remarks).</summary>
    public string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var representatives = Classes.SelectMany(c => c.Value.Select(unit => (Unit: unit, Representative: c.Key))).ToDictionary(e => e.Unit, e => e.Representative);
        var folded = new StringBuilder(value.Length);
        foreach (var unit in value)
        {
            if (Ignored.Contains(unit, StringComparison.Ordinal))
                continue;
            if (representatives.TryGetValue(unit, out var representative))
                folded.Append(representative);
            else
                folded.Append(unit);
        }

        return folded.ToString().TrimEnd(' ');
    }

    /// <summary>The fold as its data file writes it: JSON, each class's characters as a string, classes in ordinal order.</summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("collation", Collation);
            json.WriteString("kind", Kind.ToString());
            json.WriteString("ignored", Ignored);
            json.WriteStartObject("classes");
            foreach (var (representative, units) in Classes.OrderBy(c => c.Key, StringComparer.Ordinal))
                json.WriteString(representative, units);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>A fold from its data file's text.</summary>
    public static CollationFold FromJson(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        static string Text(JsonElement element) => element.GetString() ?? throw new FormatException("A fold's data file has a null where a string belongs.");
        return new CollationFold(
            Text(root.GetProperty("collation")),
            Enum.Parse<StringKind>(Text(root.GetProperty("kind"))),
            Text(root.GetProperty("ignored")),
            root.GetProperty("classes").EnumerateObject().ToEquatableMap(c => c.Name, c => Text(c.Value)));
    }

    /// <summary>Characters as a string in ordinal order, each once.</summary>
    public static string Units(IEnumerable<int> units) => new([.. units.Distinct().Order().Select(u => (char)u)]);

    /// <summary>The data file's name for a collation and kind.</summary>
    public static string FileName(string collation, StringKind kind) =>
        $"{collation}.{kind switch { StringKind.Unicode => "unicode", StringKind.CodePage => "codepage", StringKind.Utf8 => "utf8", _ => throw new ArgumentOutOfRangeException(nameof(kind)) }}";

    private static CollationFold Read(string collation, StringKind kind)
    {
        using var stream = typeof(CollationFold).Assembly.GetManifestResourceStream($"Haruspex.SqlServer.Semantics.Collations.{FileName(collation, kind)}.json")
            ?? throw new InvalidOperationException($"The fold of {collation} ({kind}) is not in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return FromJson(reader.ReadToEnd());
    }

    /// <summary>A code as hexadecimal, for messages.</summary>
    public static string Hex(int code) => code.ToString("X4", CultureInfo.InvariantCulture);
}
