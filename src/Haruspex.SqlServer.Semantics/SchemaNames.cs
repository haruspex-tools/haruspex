using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// How a name written without a schema is resolved among the project's objects, by where it is written
/// (<see cref="SchemaSearch"/>). In a module's definition SQL Server looks in the module's schema and then in <c>dbo</c>, so
/// the name means the one object it finds there, or none. Anywhere else it looks in the caller's default schema and then in
/// <c>dbo</c>, and the source can't say who calls; so when exactly one schema of the project has an object of that name, a
/// statement that runs at all has read that one, and when more do, which it reads depends on the caller.
/// </summary>
public static class SchemaNames
{
    /// <summary>
    /// What a name without a schema means among <paramref name="objects"/>, compared ignoring case: the one object of that
    /// name the search finds; several, in the order of their full names and then their kinds, so a message that lists them is
    /// stable; or none, when it means <c>dbo</c>'s, the last schema looked in.
    /// </summary>
    /// <param name="objects">The objects of the kinds the name may be.</param>
    /// <param name="name">The name as written, without a schema.</param>
    /// <param name="search">Where the name is written, which says where SQL Server looks for it.</param>
    public static SchemaNameResolution Resolve(IEnumerable<SchemaObject> objects, string name, SchemaSearch search)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(search);
        List<SchemaObject> candidates =
        [
            .. objects
                .Where(o => o.Name.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.Name.ToString(), StringComparer.Ordinal)
                .ThenBy(Kind, StringComparer.Ordinal),
        ];

        // In a module's definition, its own schema's object if it has one, and otherwise dbo's: no other schema's is read,
        // whoever calls (observed: A_name_the_procedures_schema_lacks_is_dbos_and_never_another_schemas).
        if (search.Own is { } own)
            candidates = InSchema(candidates, own) is { Count: > 0 } mine ? mine : InSchema(candidates, "dbo");

        return candidates switch
        {
            [] => new NoObject(new ObjectName("dbo", name)),
            [var one] => new OneObject(one),
            _ => new SeveralObjects([.. candidates]),
        };
    }

    private static List<SchemaObject> InSchema(List<SchemaObject> candidates, string schema) =>
        [.. candidates.Where(c => c.Name.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase))];

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
        XmlSchemaCollection => "XML schema collection",
        Sequence => "sequence",
    };
}
