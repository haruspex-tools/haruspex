using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// How a name written without a schema is resolved. SQL Server looks for it in the caller's default schema, then in
/// <c>dbo</c>, and the source can't say who calls; so when exactly one schema of the project has an object of that name, a
/// statement that runs at all has read that one, and when more do, which it reads depends on the caller. The one rule for
/// every kind of name: a table or view in FROM, a write's target, an <c>EXEC</c>, a table-valued function, an alias type.
/// </summary>
public static class SchemaNames
{
    /// <summary>
    /// What a name without a schema means among <paramref name="objects"/>: the one of that name in any schema, ignoring case;
    /// several, in the order of their full names and then their kinds, so a message that lists them is stable; or none, when it
    /// means <c>dbo</c>'s.
    /// </summary>
    /// <param name="objects">The objects of the kinds the name may be.</param>
    /// <param name="name">The name as written, without a schema.</param>
    public static SchemaNameResolution Resolve(IEnumerable<SchemaObject> objects, string name)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(name);
        List<SchemaObject> candidates =
        [
            .. objects
                .Where(o => o.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.Name.ToString(), StringComparer.Ordinal)
                .ThenBy(Kind, StringComparer.Ordinal),
        ];
        return candidates switch
        {
            [] => new NoObject(new ObjectName("dbo", name)),
            [var one] => new OneObject(one),
            _ => new SeveralObjects(candidates),
        };
    }

    /// <summary>What an object of the schema is, in a word for messages: <c>table</c>, <c>view</c>, <c>function</c> and the rest.</summary>
    /// <param name="named">The object.</param>
    public static string Kind(SchemaObject named) => named switch
    {
        Table => "table",
        View => "view",
        Synonym => "synonym",
        Function => "function",
        Procedure => "procedure",
        Trigger => "trigger",
        UserType => "type",
    };
}
