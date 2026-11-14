using System.Text;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// An element's name in <c>model.xml</c>, as its parts: <c>[sales].[order]</c> is a table, <c>[sales].[order].[status]</c> one
/// of its columns. DacFx writes every part in brackets, a <c>]</c> inside one doubled, so a part may hold a dot.
/// </summary>
internal static class ElementName
{
    /// <summary>The parts of a bracketed name, unquoted.</summary>
    /// <exception cref="FormatException">The name is not bracketed parts separated by dots.</exception>
    public static IReadOnlyList<string> Parts(string name)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < name.Length)
        {
            if (name[i] != '[')
                throw new FormatException($"'{name}' is not a name as model.xml writes one.");
            var part = new StringBuilder();
            i++;
            while (i < name.Length && !(name[i] == ']' && (i + 1 == name.Length || name[i + 1] != ']')))
            {
                part.Append(name[i]);
                i += name[i] == ']' ? 2 : 1;
            }

            parts.Add(part.ToString());
            i++;
            if (i < name.Length && name[i] != '.')
                throw new FormatException($"'{name}' is not a name as model.xml writes one.");
            i++;
        }

        return parts;
    }

    /// <summary>A schema object's name, from its first two parts.</summary>
    public static ObjectName Object(string name) => Parts(name) is [var schema, var objectName, ..]
        ? new ObjectName(schema, objectName)
        : throw new FormatException($"'{name}' names no object in a schema.");

    /// <summary>The last part of a name: a column's, a parameter's, a type's.</summary>
    public static string Last(string name) => Parts(name)[^1];

    /// <summary>A name with every part in brackets, a <c>]</c> doubled, as T-SQL quotes one: never ambiguous, whatever it holds.</summary>
    public static string Quoted(ObjectName name) => Bracket(name.Schema) + "." + Bracket(name.Name);

    private static string Bracket(string part) => "[" + part.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
