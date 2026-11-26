namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// The catalogue of an <c>xml</c> value's methods, by name: what <see cref="BuiltInFunctions"/> is for the functions, kept apart
/// because a method is called on a value, never alone. <c>nodes()</c> is read in <c>FROM</c> (<see cref="XmlNodesCall"/>), and
/// <c>modify()</c>, not listed, only in an <c>UPDATE</c>'s <c>SET</c>.
/// </summary>
public static class XmlMethods
{
    /// <summary>The method of this name, in any case; null for any other. The one place a method's name is compared.</summary>
    public static XmlMethod? Named(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var method in Enum.GetValues<XmlMethod>())
        {
            if (method.ToString().Equals(name, StringComparison.OrdinalIgnoreCase))
                return method;
        }

        return null;
    }
}
