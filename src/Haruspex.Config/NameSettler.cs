using System.Globalization;
using System.Text;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Settles the name of every procedure and object type in a document, from the naming rules and the name overrides, and
/// replaces the provisional keys inference gave the types. The IR keeps each object's SQL name as its id and each type's
/// identity, so names can change without losing what they name.
/// </summary>
/// <remarks>
/// <para><b>Names that don't shift.</b> A procedure's name comes from its own name and its schema's naming rules, and a
/// nested type's from its procedure's name and the keys leading to it (<c>GetOrder</c>, <c>GetOrderRow</c>,
/// <c>GetOrderLine</c> for <c>lines[]</c>; <c>…LinesRow</c> for a set named <c>lines</c>, <c>…Result2Row</c> for a second
/// unnamed result set; <c>PartTreeNode</c> for a
/// recursive function's node). None depends on which other objects exist.</para>
/// <para><b>Clashes are errors.</b> When two objects' names come out the same, the error names both and asks for an
/// override. A number added instead would let adding one procedure renumber another's types and break code that used
/// them.</para>
/// <para><b>Numbers only within one object.</b> Inside one procedure (or one recursive function's types), two types can
/// still get the same name: the variants of a union share their path, or a key <c>row</c> beside the row type. There a
/// number is added, in the order the procedure's results are written, because it depends only
/// on that procedure's own shape: nothing outside it can change it, and an override names either type
/// (<c>GetOrderTag2</c>).</para>
/// </remarks>
internal sealed class NameSettler(
    HaruspexConfig config,
    IReadOnlyDictionary<string, IrObjectType> types,
    IReadOnlyDictionary<string, (string Name, JsonLocation? At)> procedureNames,
    IReadOnlyDictionary<(string Object, string Path), (string Name, JsonLocation At)> pathNames,
    List<Diagnostic> problems)
{
    /// <summary>Provisional key to settled name.</summary>
    private readonly Dictionary<string, string> named = new(StringComparer.Ordinal);

    /// <summary>The provisional keys of the types whose names a <c>name</c> in <c>paths</c> gave, rather than the naming rules.</summary>
    private readonly HashSet<string> declared = new(StringComparer.Ordinal);

    /// <summary>Settled name to what holds it: the object it belongs to, and a description for errors.</summary>
    private readonly Dictionary<string, Holder> holders = new(StringComparer.Ordinal);

    private string ConfigFile => config.File;

    /// <summary>The ids of the document's query files, which are paths, named from their files' names rather than split as SQL names.</summary>
    private readonly HashSet<string> queries = new(StringComparer.Ordinal);

    private bool IsQuery(string id) => queries.Contains(id);

    /// <summary>What an object is called in a problem: <c>the procedure sales.get_order</c>, <c>the query file queries/get_order.sql</c>.</summary>
    private string Described(string id) => IsQuery(id) ? $"the query file {id}" : $"the procedure {id}";

    /// <summary>
    /// Where the naming of the object <paramref name="id"/> is set, for a problem with its name: the setting that decides it,
    /// else the file as a whole.
    /// </summary>
    private DiagnosticLocation? NamingOf(string id) =>
        IsQuery(id) ? (DiagnosticLocation?)config.NamingAt ?? config.Location
        : TrySplit(id, out var schema, out _) && config.NamingLocation(schema) is { } at ? at : config.Location;

    /// <summary>What holds a name: the object it belongs to, a description for errors, whether it is a type, and the setting to change.</summary>
    /// <param name="IsTableRow">Whether it is a table type's row, named after the type, which no setting names otherwise.</param>
    private sealed record Holder(string Owner, string Description, bool IsType, JsonLocation? At, bool IsTableRow = false);

    /// <summary>
    /// The setting a clash or a naming problem of <paramref name="id"/> points at: its name override, or else its schema's
    /// block (where a prefix or a replacement would go), or else the top-level naming.
    /// </summary>
    private JsonLocation? SettingFor(string id) =>
        procedureNames.TryGetValue(id, out var given) && given.At is not null ? given.At
        : IsQuery(id) ? config.NamingAt ?? (config.Queries.Count > 0 ? config.Queries[0].At : null)
        : TrySplit(id, out var schema, out _) ? config.Schema(schema)?.At ?? config.NamingAt : null;

    private readonly HashSet<string> reportedClashes = new(StringComparer.Ordinal);

    /// <summary>Every object's id, procedures and query files, for a clash to find each that would take its name.</summary>
    private readonly List<string> ids = [];

    /// <summary>The document with every name settled, or null when a name could not be (the problems say why).</summary>
    public IrDocument? Settle(IrDocument document, IReadOnlyList<IrResult>?[] results)
    {
        var errors = problems.Count(p => p.IsError);
        queries.UnionWith(document.Objects.Where(o => o.Kind == IrObjectKind.Query).Select(o => o.Id));
        ids.AddRange(document.Objects.Select(o => o.Id).Order(StringComparer.Ordinal));
        var objects = new List<IrObject>();
        var ordered = document.Objects.Select((o, i) => (Object: o, Results: results[i])).OrderBy(o => o.Object.Id, StringComparer.Ordinal).ToList();
        var settledNames = new List<(IrObject Object, IReadOnlyList<IrResult>? Results, string Name)>();

        // Every procedure's own name and every name an override gives are claimed first, so that a generated type name
        // that happens to equal one is reported as a clash rather than taking it.
        foreach (var (obj, objectResults) in ordered)
        {
            // A procedure whose own name clashes is not walked: its types' names build on that name, so each would
            // clash too, and one error says it all.
            var name = ProcedureName(obj.Id);
            if (name is null || !Claim(name, new Holder(obj.Id, Described(obj.Id), false, SettingFor(obj.Id))))
                continue;
            foreach (var ((owner, path), (pathName, at)) in pathNames.Where(p => p.Key.Object == obj.Id).OrderBy(p => p.Key.Path, StringComparer.Ordinal))
                Claim(pathName, new Holder(owner, $"the type at {path} in {owner}", true, at));
            settledNames.Add((obj, objectResults, name));
        }

        foreach (var (obj, objectResults, name) in settledNames)
        {
            if (objectResults is not null)
            {
                for (var i = 0; i < objectResults.Count; i++)
                {
                    var stem = Stem(name, objectResults, i);
                    Root(objectResults[i].Type, stem, $"results[{i.ToString(CultureInfo.InvariantCulture)}]", obj.Id);
                }
            }

            // A table-valued parameter's rows are a type of the table type's, named after it, once for every parameter of it.
            // What a parameter declared to hold JSON parses to is named after the object and the parameter, as a result's row is
            // after the object: PlaceOrderOrder for shop.place_order's @order.
            foreach (var parameter in obj.Parameters)
            {
                Walk(parameter.Type, name, $"parameters.{parameter.Name}", obj.Id);
                if (parameter.Content is { } content)
                    Walk(content, name + NamingRules.Part(parameter.Name.TrimStart('@')), $"parameters.{parameter.Name}<json>", obj.Id);
            }

            objects.Add(obj with
            {
                Name = name,
                Parameters = [.. obj.Parameters.Select(p => p with { Type = Rename(p.Type), Content = p.Content is { } content ? Rename(content) : null })],
                Results = objectResults is null ? null : [.. objectResults.Select(r => r with { Type = Rename(r.Type) })],
            });
        }

        if (problems.Count(p => p.IsError) > errors)
            return null;

        // Types no result reaches any more (replaced by an override's import) are dropped.
        var renamed = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
        foreach (var (key, type) in types)
        {
            if (named.TryGetValue(key, out var name))
                renamed[name] = type with { Fields = [.. type.Fields.Select(f => f with { Type = Rename(f.Type) })], NameDeclared = declared.Contains(key) };
        }

        return document with { Objects = [.. objects], Types = renamed };
    }

    /// <summary>
    /// What the names of the types in a result set start with: a named set's name after its procedure's (<c>GetOrderLines</c>,
    /// so <c>GetOrderLinesRow</c>), whatever the number of sets; the procedure's own name for one unnamed set; and for several
    /// unnamed sets, its place (<c>GetOrderResult2</c>). A named set's types then keep their names when a set is added.
    /// </summary>
    private static string Stem(string name, IReadOnlyList<IrResult> results, int index) =>
        results[index].Name is { } set ? name + NamingRules.Part(set)
        : results.Count == 1 ? name
        : name + "Result" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private string? ProcedureName(string id)
    {
        var generated = Generated(id, out var why);
        if (procedureNames.TryGetValue(id, out var given))
        {
            if (given.Name == generated)
                problems.Add(ConfigCodes.NameAgrees.At(given.At, $"the name \"{given.Name}\" is what the naming rules give {id} already. Remove it."));
            return given.Name;
        }

        if (generated is null)
            problems.Add(ConfigCodes.ProcedureUnnamed.At(NamingOf(id), $"{Described(id)} cannot be named: {why}"));
        return generated;
    }

    private string? Generated(string id, out string? why)
    {
        // A query file is named from its file's name, by the top-level rules: it has no schema.
        if (IsQuery(id))
            return config.Naming.Name(Stem(id), config.Reserved, out why, otherwise: "rename the query file");
        if (!TrySplit(id, out var schema, out var name))
        {
            why = $"'{id}' is not a two-part name.";
            return null;
        }

        return config.NamingFor(schema).Name(name, config.Reserved, out why);
    }

    /// <summary>A result's root: its object, or its rows' object, is <c>…Row</c>, and nested names build on the stem.</summary>
    private void Root(IrType type, string stem, string path, string owner)
    {
        switch (type)
        {
            case IrRef reference when NodeOf(reference) is null:
                Object(reference.Name, stem + "Row", stem, path, owner);
                break;
            case IrArray { Items: IrRef reference } when NodeOf(reference) is null:
                Object(reference.Name, stem + "Row", stem, path + "[]", owner);
                break;

            // A result several statements may return (IR 0.6): each variant's object is a row of it, numbered after the first.
            case IrUnion union when union.Variants.All(v => (v is IrRef r && NodeOf(r) is null) || (v is IrArray { Items: IrRef i } && NodeOf(i) is null)):
                foreach (var variant in union.Variants)
                {
                    if (variant is IrRef reference)
                        Object(reference.Name, stem + "Row", stem, path, owner);
                    else
                        Object(((IrRef)((IrArray)variant).Items).Name, stem + "Row", stem, path + "[]", owner);
                }

                break;
            default:
                Walk(type, stem, path, owner);
                break;
        }
    }

    private void Walk(IrType type, string name, string path, string owner)
    {
        switch (type)
        {
            case IrRef reference when NodeOf(reference) is { } node:
                Node(reference.Name, node);
                break;
            case IrRef reference when types[reference.Name].Identity is { Owner: IrTypeOwner.TableType } row:
                TableRow(reference.Name, row);
                break;
            case IrRef reference:
                Object(reference.Name, name, null, path, owner);
                break;
            default:
                foreach (var child in IrTypeTree.Children(type))
                    Walk(child.Type, name, child.Segment is { } segment ? path + IrPath.Render([segment]) : path, owner);
                break;
        }
    }

    /// <param name="key">The type's provisional key.</param>
    /// <param name="generated">The name the rules give it.</param>
    /// <param name="stem">What its fields' types' names start with, when not its own name (a result's row).</param>
    private void Object(string key, string generated, string? stem, string path, string owner)
    {
        if (named.ContainsKey(key))
            return;

        string name;
        if (pathNames.TryGetValue((owner, path), out var given))
        {
            name = given.Name;
            declared.Add(key);
            if (name == generated)
                problems.Add(ConfigCodes.NameAgrees.At(given.At, $"the name \"{name}\" is what the naming rules give the type at {path} already. Remove it."));
        }
        else
        {
            name = Unique(generated, new Holder(owner, $"the type at {path} in {owner}", true, SettingFor(owner)));
        }

        named[key] = name;
        Fields(types[key], stem ?? name, path, owner);
    }

    /// <summary>A recursive function's node, named after the function, once for the whole document.</summary>
    private void Node(string key, IrTypeIdentity identity)
    {
        if (named.ContainsKey(key))
            return;

        var type = types[key];
        var function = identity.Object;
        var generated = Generated(function, out var why);
        if (generated is null)
        {
            problems.Add(ConfigCodes.TypeUnnamed.At(NamingOf(function),
                $"the recursive function {function}'s type cannot be named: {why} Change the naming rules so that they name it."));
            named[key] = key;
            return;
        }

        var name = generated + "Node";
        Claim(name, new Holder(function, $"the type of the recursive function {function}", true, SettingFor(function)));
        named[key] = name;
        Fields(type, name, identity.Path, function);
    }

    /// <summary>
    /// A table type's row, named after the type as a procedure of its schema would be, with <c>Row</c> after it
    /// (<c>LineListRow</c> for <c>sales.line_list</c>), once for the whole document, whichever parameters take it.
    /// </summary>
    private void TableRow(string key, IrTypeIdentity identity)
    {
        if (named.ContainsKey(key))
            return;

        var tableType = identity.Object;
        string? generated = null;
        var why = $"'{tableType}' is not a two-part name.";
        if (TrySplit(tableType, out var schema, out var within))
            generated = config.NamingFor(schema).Name(within, config.Reserved, out why);
        if (generated is null)
        {
            problems.Add(ConfigCodes.TypeUnnamed.At(NamingOf(tableType),
                $"the rows of the table type {tableType} cannot be named: {why} Change the naming rules so that they name it."));
            named[key] = key;
            return;
        }

        var name = generated + "Row";
        Claim(name, new Holder(tableType, $"the rows of the table type {tableType}", true, SettingFor(tableType), IsTableRow: true));
        named[key] = name;
    }

    private void Fields(IrObjectType type, string stem, string path, string owner)
    {
        foreach (var field in type.Fields)
        {
            var part = NamingRules.Part(field.Name);
            var child = stem + (field.Type is IrArray or IrRecord ? NamingRules.Singular(part) : part);
            Walk(field.Type, child, path + IrPath.Key(field.Name), owner);
        }
    }

    /// <summary>
    /// The identity of a recursive function's node (its own type, as opposed to an object type nested inside it), or null
    /// when <paramref name="reference"/> is not one.
    /// </summary>
    private IrTypeIdentity? NodeOf(IrRef reference) =>
        types[reference.Name].Identity is { Owner: IrTypeOwner.Function } identity && identity.Path == $"<{identity.Object}>" ? identity : null;

    /// <summary>The name, or the first free one with a number when the same object already holds it.</summary>
    private string Unique(string name, Holder claimant)
    {
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : name + n.ToString(CultureInfo.InvariantCulture);
            if (!holders.TryGetValue(candidate, out var holder))
            {
                holders[candidate] = claimant;
                return candidate;
            }

            if (holder.Owner != claimant.Owner)
            {
                Clash(candidate, holder, claimant);
                return candidate;
            }
        }
    }

    private bool Claim(string name, Holder claimant)
    {
        if (holders.TryGetValue(name, out var holder))
        {
            Clash(name, holder, claimant);
            return false;
        }

        holders[name] = claimant;
        return true;
    }

    /// <summary>
    /// A clash, with the advice that fits it: a prefix tells two schemas apart, but not two objects of one schema, where a
    /// replacement rule or a name override does; a type in a procedure's results is named by a path override.
    /// </summary>
    private void Clash(string name, Holder first, Holder second)
    {
        if (!reportedClashes.Add(name))
            return;
        if (first.IsTableRow || second.IsTableRow)
        {
            TableRowClash(name, first, second);
            return;
        }

        var schema = TrySplit(first.Owner, out var a, out _) ? a : "";
        var sameSchema = !IsQuery(first.Owner) && !IsQuery(second.Owner)
            && TrySplit(second.Owner, out var b, out _) && string.Equals(schema, b, StringComparison.OrdinalIgnoreCase);

        // Two procedures of one schema: every procedure of it that would take the name is named, since a third would still
        // clash once two were parted.
        var procedures = sameSchema && !first.IsType && !second.IsType ? Alike(name, schema) : null;
        var ways = IsQuery(first.Owner) || IsQuery(second.Owner) ? Ways(first, second)
            : procedures is not null ? Replacing(schema, procedures)
            : SqlNames(first, second, sameSchema);
        var named = procedures is { Count: > 2 }
            ? "would name " + string.Join(", ", procedures.SkipLast(1).Select(Described)) + " and " + Described(procedures[^1])
            : $"would name both {first.Description} and {second.Description}";
        var noFile = ConfigFile.Length == 0 ? " These settings go in a haruspex.json beside the project, which it doesn't have yet." : "";
        problems.Add(ConfigCodes.NamesClash.At((DiagnosticLocation?)second.At ?? (DiagnosticLocation?)first.At ?? config.Location,
            $"\"{name}\" {named}. Generated names must not clash: {string.Join(", or ", ways)}.{noFile}"));
    }

    /// <summary>
    /// A clash with a table type's row, whose name is the type's and which no setting names otherwise: a schema that holds only
    /// table types can't be given a prefix (no procedure of it is selected), so the other is the one renamed, or failing that, a
    /// table type.
    /// </summary>
    private void TableRowClash(string name, Holder first, Holder second)
    {
        var other = first.IsTableRow ? second : first;
        List<string> ways = other switch
        {
            { IsTableRow: true } => ["rename one of the table types, since a table type's rows are named after it and no setting names them otherwise"],
            _ when IsQuery(other.Owner) => ["rename the query file"],
            { IsType: true } => ["give the type its own name with a \"paths\" override of its procedure in \"procedures\"", "give its procedure's schema a \"prefix\" in \"schemas\""],
            _ => ["give the procedure its own name with a \"name\" override in \"procedures\"", "give its schema a \"prefix\" in \"schemas\""],
        };
        problems.Add(ConfigCodes.NamesClash.At((DiagnosticLocation?)other.At ?? config.Location,
            $"\"{name}\" would name both {first.Description} and {second.Description}. Generated names must not clash: {string.Join(", or ", ways)}."));
    }

    /// <summary>A procedure's or query file's name, given or generated, without reporting anything about it; null when it has none.</summary>
    private string? NameOf(string id) => procedureNames.TryGetValue(id, out var given) ? given.Name : Generated(id, out _);

    private bool InSchema(string id, string schema) =>
        !IsQuery(id) && TrySplit(id, out var its, out _) && string.Equals(its, schema, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every procedure of <paramref name="schema"/> that would take <paramref name="name"/>, by id.</summary>
    private List<string> Alike(string name, string schema) => [.. ids.Where(id => InSchema(id, schema) && NameOf(id) == name)];

    /// <summary>
    /// The ways to part two names from SQL that aren't two procedures of one schema: a prefix for two schemas, and an override.
    /// A type's name is built on its procedure's, so no rule is built from it (<see cref="Replacing"/>).
    /// </summary>
    private static List<string> SqlNames(Holder first, Holder second, bool sameSchema) =>
    [
        .. sameSchema ? Array.Empty<string>() : ["give one of their schemas a \"prefix\" in \"schemas\""],
        first.IsType || second.IsType
            ? "give one its own name with an override in \"procedures\" (\"name\" for a procedure, \"paths\": { \"<path>\": { \"name\": … } } for a type in its results)"
            : "give one of them its own name with a \"name\" override in \"procedures\"",
    ];

    /// <summary>
    /// The ways to part two procedures of one schema: the naming <c>replace</c> rule built from where their names differ, checked
    /// to part them, and an override; or the override alone, saying why no single rule can (<see cref="ReplacementHint"/>). A
    /// rule goes after the schema's own, when it has some, which is where it was checked.
    /// </summary>
    private List<string> Replacing(string schema, List<string> procedures)
    {
        const string Override = "give one of them its own name with a \"name\" override in \"procedures\"";

        // What the rules name, and so what a rule renames: the schema's procedures without a name override. The rest keep
        // their names, which a rule must not give one of these.
        bool ByRules(string id) => InSchema(id, schema) && !procedureNames.ContainsKey(id);
        var clashing = procedures.Where(ByRules).ToList();
        if (clashing.Count == 0)
            return [Override];
        var taken = ids.Where(id => !ByRules(id)).Select(NameOf).OfType<string>().ToHashSet(StringComparer.Ordinal);
        if (ReplacementHint.For(config.NamingFor(schema), config.Reserved, [.. clashing.Select(Within)], [.. ids.Where(id => ByRules(id) && !clashing.Contains(id)).Select(Within)], taken, out var whyNot)
            is not { } suggestion)
        {
            return [$"{Override} ({whyNot})"];
        }

        var to = config.Schema(schema)?.Naming?.Replace is { Count: > 0 } ? $"add a \"replace\" rule to the \"naming\" of schemas.{schema}" : "add a naming \"replace\" rule";
        return [$"{to} for {suggestion.Where}, such as {suggestion.Rule} with a word of your own for \"{suggestion.Word}\"", Override];
    }

    /// <summary>A procedure's name within its schema.</summary>
    private static string Within(string id) => TrySplit(id, out _, out var name) ? name : id;

    /// <summary>
    /// The ways to part a query file's name from another's: a query file is named by its file, which is renamed, and a procedure
    /// beside it by an override; neither is renamed by haruspex (decision 0059).
    /// </summary>
    private List<string> Ways(Holder first, Holder second)
    {
        var ways = new List<string> { "rename the query file" };
        if (new[] { first, second }.Any(h => !IsQuery(h.Owner) && !h.IsType))
            ways.Add("give the procedure its own name with a \"name\" override in \"procedures\", or leave it out of \"schemas\"");
        if (first.IsType || second.IsType)
            ways.Add("give the type its own name with a \"paths\" override of its procedure in \"procedures\"");
        return ways;
    }

    /// <summary>A query file's name without its folder and extension: <c>getOrderCard</c> for <c>queries/getOrderCard.sql</c>.</summary>
    private static string Stem(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private IrType Rename(IrType type) =>
        IrTypeTree.RenameReferences(type, key => named.TryGetValue(key, out var name) ? name : key);

    /// <summary>Splits an id written by the front end (<c>sales.get_order</c>, <c>[Ledger.].GetEntryList</c>).</summary>
    private static bool TrySplit(string id, out string schema, out string name)
    {
        var parts = new List<string>();
        var part = new StringBuilder();
        for (var i = 0; i < id.Length; i++)
        {
            if (id[i] == '[')
            {
                for (i++; i < id.Length; i++)
                {
                    if (id[i] == ']' && i + 1 < id.Length && id[i + 1] == ']')
                    {
                        part.Append(']');
                        i++;
                    }
                    else if (id[i] == ']')
                    {
                        break;
                    }
                    else
                    {
                        part.Append(id[i]);
                    }
                }
            }
            else if (id[i] == '.')
            {
                parts.Add(part.ToString());
                part.Clear();
            }
            else
            {
                part.Append(id[i]);
            }
        }

        parts.Add(part.ToString());
        (schema, name) = parts.Count == 2 ? (parts[0], parts[1]) : ("", "");
        return parts.Count == 2;
    }
}
