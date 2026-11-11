using System.Globalization;
using System.Text;
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
/// <c>GetOrderLine</c> for <c>lines[]</c>; <c>…Result2Row</c> for a second result set; <c>PartTreeNode</c> for a
/// recursive function's node). None depends on which other objects exist.</para>
/// <para><b>Clashes are errors.</b> When two objects' names come out the same, the error names both and asks for an
/// override. Until slice 4 a number was added instead, so adding a procedure could renumber another's types and break code
/// that used them.</para>
/// <para><b>Numbers only within one object.</b> Inside one procedure (or one recursive function's types), two types can
/// still get the same name: the variants of a union share their path, or a key <c>row</c> beside the row type. There a
/// number is added, in the order the procedure's results are written, because it depends only
/// on that procedure's own shape: nothing outside it can change it, and an override names either type
/// (<c>GetOrderTag2</c>).</para>
/// </remarks>
internal sealed class NameSettler(
    HaruspexConfig config,
    IReadOnlyDictionary<string, IrObjectType> types,
    IReadOnlyDictionary<string, (string Name, ConfigLocation? At)> procedureNames,
    IReadOnlyDictionary<(string Object, string Path), (string Name, ConfigLocation At)> pathNames,
    List<ConfigProblem> problems)
{
    /// <summary>Provisional key to settled name.</summary>
    private readonly Dictionary<string, string> named = new(StringComparer.Ordinal);

    /// <summary>Settled name to what holds it: the object it belongs to, and a description for errors.</summary>
    private readonly Dictionary<string, Holder> holders = new(StringComparer.Ordinal);

    private string ConfigFile => config.File;

    /// <summary>What holds a name: the object it belongs to, a description for errors, whether it is a type, and the setting to change.</summary>
    private sealed record Holder(string Owner, string Description, bool IsType, ConfigLocation? At);

    /// <summary>
    /// The setting a clash or a naming problem of <paramref name="id"/> points at: its name override, or else its schema's
    /// block (where a prefix or a replacement would go), or else the top-level naming.
    /// </summary>
    private ConfigLocation? SettingFor(string id) =>
        procedureNames.TryGetValue(id, out var given) && given.At is not null
            ? given.At
            : TrySplit(id, out var schema, out _) ? config.Schema(schema)?.At ?? config.NamingAt : null;

    private readonly HashSet<string> reportedClashes = new(StringComparer.Ordinal);

    /// <summary>The document with every name settled, or null when a name could not be (the problems say why).</summary>
    public IrDocument? Settle(IrDocument document, IReadOnlyList<IrResult>?[] results)
    {
        var errors = problems.Count(p => p.Severity == ConfigSeverity.Error);
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
            if (name is null || !Claim(name, new Holder(obj.Id, $"the procedure {obj.Id}", false, SettingFor(obj.Id))))
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
                    var stem = objectResults.Count == 1 ? name : name + "Result" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    Root(objectResults[i].Type, stem, $"results[{i.ToString(CultureInfo.InvariantCulture)}]", obj.Id);
                }
            }

            objects.Add(obj with
            {
                Name = name,
                Parameters = [.. obj.Parameters.Select(p => p with { Type = Rename(p.Type) })],
                Results = objectResults?.Select(r => r with { Type = Rename(r.Type) }).ToList(),
            });
        }

        if (problems.Count(p => p.Severity == ConfigSeverity.Error) > errors)
            return null;

        // Types no result reaches any more (replaced by an override's import) are dropped.
        var renamed = new Dictionary<string, IrObjectType>(StringComparer.Ordinal);
        foreach (var (key, type) in types)
        {
            if (named.TryGetValue(key, out var name))
                renamed[name] = type with { Fields = [.. type.Fields.Select(f => f with { Type = Rename(f.Type) })] };
        }

        return document with { Objects = objects, Types = renamed };
    }

    private string? ProcedureName(string id)
    {
        var generated = Generated(id, out var why);
        if (procedureNames.TryGetValue(id, out var given))
        {
            if (given.Name == generated)
                problems.Add(new ConfigProblem(ConfigSeverity.Warning, ConfigFile, given.At, $"the name \"{given.Name}\" is what the naming rules give {id} already. Remove it."));
            return given.Name;
        }

        if (generated is null)
            problems.Add(new ConfigProblem(ConfigSeverity.Error, ConfigFile, TrySplit(id, out var schema, out _) ? config.NamingLocation(schema) : null, $"the procedure {id} cannot be named: {why}"));
        return generated;
    }

    private string? Generated(string id, out string? why)
    {
        if (!TrySplit(id, out var schema, out var name))
        {
            why = $"'{id}' is not a two-part name.";
            return null;
        }

        return config.NamingFor(schema).Name(name, out why);
    }

    /// <summary>A result's root: its object, or its rows' object, is <c>…Row</c>, and nested names build on the stem.</summary>
    private void Root(IrType type, string stem, string path, string owner)
    {
        switch (type)
        {
            case IrRef reference when !IsNode(reference):
                Object(reference.Name, stem + "Row", stem, path, owner);
                break;
            case IrArray { Items: IrRef reference } when !IsNode(reference):
                Object(reference.Name, stem + "Row", stem, path + "[]", owner);
                break;

            // A result several statements may return (IR 0.6): each variant's object is a row of it, numbered after the first.
            case IrUnion union when union.Variants.All(v => (v is IrRef r && !IsNode(r)) || (v is IrArray { Items: IrRef i } && !IsNode(i))):
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
            case IrRef reference when IsNode(reference):
                Node(reference.Name);
                break;
            case IrRef reference:
                Object(reference.Name, name, null, path, owner);
                break;
            case IrArray array:
                Walk(array.Items, name, path + "[]", owner);
                break;
            case IrTuple tuple:
                for (var i = 0; i < tuple.Items.Count; i++)
                    Walk(tuple.Items[i].Type, name, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", owner);
                break;
            case IrRecord record:
                Walk(record.Values, name, path + "{}", owner);
                break;
            case IrUnion union:
                foreach (var variant in union.Variants)
                    Walk(variant, name, path, owner);
                break;
            case IrJsonText { Content: { } content }:
                Walk(content, name, path + "<json>", owner);
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
            if (name == generated)
                problems.Add(new ConfigProblem(ConfigSeverity.Warning, ConfigFile, given.At, $"the name \"{name}\" is what the naming rules give the type at {path} already. Remove it."));
        }
        else
        {
            name = Unique(generated, new Holder(owner, $"the type at {path} in {owner}", true, SettingFor(owner)));
        }

        named[key] = name;
        Fields(types[key], stem ?? name, path, owner);
    }

    /// <summary>A recursive function's node, named after the function, once for the whole document.</summary>
    private void Node(string key)
    {
        if (named.ContainsKey(key))
            return;

        var type = types[key];
        var function = type.Identity!.Object;
        var generated = Generated(function, out var why);
        if (generated is null)
        {
            problems.Add(new ConfigProblem(ConfigSeverity.Error, ConfigFile, TrySplit(function, out var schema, out _) ? config.NamingLocation(schema) : null,
                $"the recursive function {function}'s type cannot be named: {why} Change the naming rules so that they name it."));
            named[key] = key;
            return;
        }

        var name = generated + "Node";
        Claim(name, new Holder(function, $"the type of the recursive function {function}", true, SettingFor(function)));
        named[key] = name;
        Fields(type, name, type.Identity.Path, function);
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

    /// <summary>A recursive function's node: its own type, as opposed to an object type nested inside it.</summary>
    private bool IsNode(IrRef reference) =>
        types[reference.Name].Identity is { Owner: IrTypeOwner.Function } identity && identity.Path == $"<{identity.Object}>";

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

        var sameSchema = TrySplit(first.Owner, out var a, out _) && TrySplit(second.Owner, out var b, out _) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var ways = new List<string>
        {
            sameSchema
                ? "add a naming \"replace\" rule that tells their names apart (such as { \"pattern\": \"#$\", \"with\": \"Row\" })"
                : "give one of their schemas a \"prefix\" in \"schemas\"",
            first.IsType || second.IsType
                ? "give one its own name with an override in \"procedures\" (\"name\" for a procedure, \"paths\": { \"<path>\": { \"name\": … } } for a type in its results)"
                : "give one of them its own name with a \"name\" override in \"procedures\"",
        };
        var noFile = ConfigFile.Length == 0 ? " These settings go in a haruspex.json beside the project, which it doesn't have yet." : "";
        problems.Add(new ConfigProblem(ConfigSeverity.Error, ConfigFile, second.At ?? first.At,
            $"\"{name}\" would name both {first.Description} and {second.Description}. Generated names must not clash: {string.Join(", or ", ways)}.{noFile}"));
    }

    private IrType Rename(IrType type) => type switch
    {
        IrRef reference => new IrRef(named.TryGetValue(reference.Name, out var name) ? name : reference.Name),
        IrArray array => array with { Items = Rename(array.Items) },
        IrTuple tuple => new IrTuple([.. tuple.Items.Select(e => e with { Type = Rename(e.Type) })]),
        IrRecord record => record with { Values = Rename(record.Values) },
        IrUnion union => new IrUnion([.. union.Variants.Select(Rename)]),
        IrJsonText { Content: { } content } => new IrJsonText(Rename(content)),
        _ => type,
    };

    /// <summary>Splits an id written by the front end (<c>sales.get_order</c>, <c>[RDF.].GetPropertyList</c>).</summary>
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
