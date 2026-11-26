namespace Haruspex.SqlServer.Model;

/// <summary>
/// The descriptions a database keeps of its tables, views, procedures, their columns and their parameters, found in its extended
/// properties under the names the config file gives (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// Names are compared ignoring case, as <see cref="ObjectName"/> compares them.
/// </summary>
/// <param name="objects">Each table's, view's and procedure's own.</param>
/// <param name="parts">Each column's and parameter's, by its object and its name (a parameter's with its <c>@</c>).</param>
public sealed class Descriptions(IEnumerable<KeyValuePair<ObjectName, string>> objects, IEnumerable<KeyValuePair<(ObjectName Object, string Part), string>> parts)
{
    private readonly Dictionary<ObjectName, string> objects = new(objects ?? throw new ArgumentNullException(nameof(objects)));
    private readonly Dictionary<(ObjectName Object, string Part), string> parts = new(parts ?? throw new ArgumentNullException(nameof(parts)), PartComparer.Instance);

    /// <summary>None at all.</summary>
    public static Descriptions None { get; } = new([], []);

    /// <summary>How many there are, of objects and their parts together.</summary>
    public int Count => objects.Count + parts.Count;

    /// <summary>A table's, view's or procedure's own description; null when it has none.</summary>
    public string? Of(ObjectName name) => objects.GetValueOrDefault(name);

    /// <summary>A column's or a parameter's description (a parameter by its name with its <c>@</c>); null when it has none.</summary>
    public string? Of(ObjectName name, string part) => parts.GetValueOrDefault((name, part));

    private sealed class PartComparer : IEqualityComparer<(ObjectName Object, string Part)>
    {
        public static PartComparer Instance { get; } = new();

        public bool Equals((ObjectName Object, string Part) x, (ObjectName Object, string Part) y) =>
            x.Object.Equals(y.Object) && StringComparer.OrdinalIgnoreCase.Equals(x.Part, y.Part);

        public int GetHashCode((ObjectName Object, string Part) obj) =>
            HashCode.Combine(obj.Object, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Part));
    }
}
