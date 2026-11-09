using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Applies a procedure's path overrides to the IR, as transformations of the document inference produced, never by
/// reaching back into inference (haruspex-design.md, "Layers and what each may know"): they work the same however the
/// IR was made, and an emitter never needs to know they existed.
/// </summary>
/// <remarks>
/// <para><b>Overrides stay runtime-checked.</b> A narrowing override (<c>nullable: false</c>, exact values, an imported
/// schema) becomes part of the type, and the generated validator checks every value against it, so a wrong override
/// fails loudly when the data disagrees; it is never a silent cast. Each claim is also listed in the IR's
/// <c>overrides</c>, so what the database proves and what a person asserted stay distinguishable.</para>
/// <para><b>The file shrinks.</b> An override whose path no longer exists is an error; one the inference already agrees
/// with is a warning, so it can be removed as the SQL improves.</para>
/// </remarks>
internal sealed class OverrideApplier(
    string file,
    Dictionary<string, IrObjectType> types,
    List<IrUnknown> unknowns,
    List<IrOverride> claims,
    List<ConfigProblem> problems,
    Func<string, string> importFrom)
{
    /// <summary>The names given by path overrides, by object and canonical path, for the namer.</summary>
    public Dictionary<(string Object, string Path), (string Name, ConfigLocation At)> Names { get; } = [];

    /// <summary>Applies one override to an object's results, returning them changed.</summary>
    public IReadOnlyList<IrResult>? Apply(IrObject obj, IReadOnlyList<IrResult>? results, PathOverride entry)
    {
        // The reader has checked the path's syntax already.
        if (!IrPath.TryParse(entry.Path, out var written, out _))
            return results;
        try
        {
            if (results is null)
                throw new PathException($"the results of {obj.Id} are unknown (see the unknowns in the IR), so no path in them exists.");

            // A path is relative to the only result, or names one: results[1][].id.
            int index;
            IReadOnlyList<IrPathSegment> rest;
            if (written is [IrPathKey { Key: "results" }, IrPathIndex given, ..])
            {
                if (given.Index >= results.Count)
                    throw new PathException($"{obj.Id} returns {Count(results.Count, "result set")}, so there is no results[{given.Index}].");
                (index, rest) = (given.Index, written.Skip(2).ToList());
            }
            else if (results.Count == 1)
            {
                (index, rest) = (0, written);
            }
            else
            {
                throw new PathException($"{obj.Id} returns {Count(results.Count, "result set")}, so the path must start with the one it means, as results[0].");
            }

            var canonical = IrPath.Render([new IrPathKey("results"), new IrPathIndex(index), .. rest]);
            var result = results[index];
            var slot = new Slot(result.Type, result.AbsentBecause, CanBeNull: true);
            var changed = Edit(slot, rest, 0, canonical, s => Settle(obj.Id, canonical, s, entry));
            var list = results.ToList();
            list[index] = result with { Type = changed.Type, AbsentBecause = changed.Reasons };
            return list;
        }
        catch (PathException e)
        {
            problems.Add(new ConfigProblem(ConfigSeverity.Error, file, entry.At, e.Message));
            return results;
        }
    }

    /// <summary>A place a value is: its type, why it can be null, and whether it can be null at all.</summary>
    private sealed record Slot(IrType Type, IReadOnlyList<IrNullReason> Reasons, bool CanBeNull);

    /// <summary>What the override says about the value at its path.</summary>
    private Slot Settle(string objectId, string path, Slot slot, PathOverride entry)
    {
        var at = entry.At;
        if (entry.NotNull)
        {
            if (!slot.CanBeNull)
                throw new PathException($"the value at {path} is what JSON text holds, which is never null itself; nothing can be declared about its nullability.");
            if (slot.Reasons.Count == 0)
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Warning, file, at, $"\"nullable\": false agrees with the inference, which already proves the value at {path} is never null. Remove it."));
            }
            else
            {
                slot = slot with { Reasons = [] };
                claims.Add(new IrOverride(objectId, path, IrOverrideSettings.NotNull, entry.Why));
            }
        }

        if (entry.ExactValues)
        {
            if (slot.Type is not IrScalar { Facets.Values: { } set } scalar)
            {
                throw new PathException($"the value at {path} has no value set to declare exact. haruspex infers one from a CHECK constraint on a column the value passes through unchanged; "
                    + "canonical value sets for other cases are planned, not done.");
            }

            if (set.Match == IrValueMatch.Exact)
            {
                problems.Add(new ConfigProblem(ConfigSeverity.Warning, file, at, $"\"values\": \"exact\" agrees with the inference, which already proves the set at {path} exact. Remove it."));
            }
            else
            {
                slot = slot with { Type = scalar with { Facets = scalar.Facets! with { Values = set with { Match = IrValueMatch.Exact } } } };
                claims.Add(new IrOverride(objectId, path, IrOverrideSettings.ExactValues, entry.Why));
            }
        }

        if (entry.Type is { } type)
        {
            IrType imported = new IrImport(type.TrimEnd('[', ']'), importFrom(entry.Import!));
            for (var depth = (type.Length - type.TrimEnd('[', ']').Length) / 2; depth > 0; depth--)
                imported = new IrArray(imported);
            slot = slot with { Type = imported };
            claims.Add(new IrOverride(objectId, path, IrOverrideSettings.Type, entry.Why));

            // What was unknown at or under the path is now declared.
            unknowns.RemoveAll(u => u.Object == objectId && (u.Path == path || (u.Path.StartsWith(path, StringComparison.Ordinal) && u.Path[path.Length] is '.' or '[' or '{' or '<')));
        }

        if (entry.Name is { } name)
        {
            if (slot.Type is not IrRef reference)
            {
                var hint = slot.Type is IrArray { Items: IrRef } ? $" To name its items, use the path \"{entry.Path}[]\"." : "";
                throw new PathException($"only an object type can be named, and the value at {path} is {Describe(slot.Type)}.{hint}");
            }

            if (types[reference.Name].Identity?.Owner == IrTypeOwner.Function)
                throw new PathException(Shared(path, types[reference.Name]));
            Names[(objectId, path)] = (name, at);
        }

        return slot;
    }

    /// <summary>Follows the path from <paramref name="slot"/>, editing the value at its end.</summary>
    private Slot Edit(Slot slot, IReadOnlyList<IrPathSegment> path, int i, string whole, Func<Slot, Slot> settle)
    {
        if (i == path.Count)
            return settle(slot);

        var segment = path[i];
        var here = Here(whole, path, i);
        switch (segment, slot.Type)
        {
            case (IrPathKey key, IrRef reference):
                var obj = types[reference.Name];
                if (obj.Identity?.Owner == IrTypeOwner.Function)
                    throw new PathException(Shared(whole, obj));
                var position = obj.Fields.ToList().FindIndex(f => f.Name == key.Key);
                if (position < 0)
                {
                    throw new PathException($"the path {whole} does not exist: the object at {here} has no key \"{key.Key}\". Its keys are "
                        + string.Join(", ", obj.Fields.Select(f => $"\"{f.Name}\"")) + ".");
                }

                var field = obj.Fields[position];
                var edited = Edit(new Slot(field.Type, field.NullableBecause, CanBeNull: true), path, i + 1, whole, settle);
                var fields = obj.Fields.ToList();
                fields[position] = field with { Type = edited.Type, NullableBecause = edited.Reasons };
                types[reference.Name] = obj with { Fields = fields };
                return slot;
            case (IrPathItems, IrArray array):
                var items = Edit(new Slot(array.Items, array.ItemsNullableBecause, CanBeNull: true), path, i + 1, whole, settle);
                return slot with { Type = array with { Items = items.Type, ItemsNullableBecause = items.Reasons } };
            case (IrPathIndex index, IrTuple tuple) when index.Index < tuple.Items.Count:
                var element = tuple.Items[index.Index];
                var changed = Edit(new Slot(element.Type, element.NullableBecause, CanBeNull: true), path, i + 1, whole, settle);
                var elements = tuple.Items.ToList();
                elements[index.Index] = new IrElement(changed.Type, changed.Reasons);
                return slot with { Type = new IrTuple(elements) };
            case (IrPathValues, IrRecord record):
                var values = Edit(new Slot(record.Values, record.ValuesNullableBecause, CanBeNull: true), path, i + 1, whole, settle);
                return slot with { Type = new IrRecord(values.Type, values.Reasons) };
            case (IrPathJson, IrJsonText { Content: { } content }):
                var parsed = Edit(new Slot(content, [], CanBeNull: false), path, i + 1, whole, settle);
                return slot with { Type = new IrJsonText(parsed.Type) };
            case (_, IrUnion):
                throw new PathException($"the path {whole} passes through a value of several types at {here}, and a path can't choose between them.");
            default:
                throw new PathException($"the path {whole} does not exist: the value at {here} is {Describe(slot.Type)}, so it has no {Describe(segment)}.");
        }
    }

    private static string Here(string whole, IReadOnlyList<IrPathSegment> path, int i)
    {
        // The canonical path is results[n] followed by the segments; the part before segment i is where we are.
        var rendered = IrPath.Render(path.Take(i));
        var prefix = whole[..(whole.Length - IrPath.Render(path).Length)];
        return prefix + (rendered.Length > 0 && rendered[0] is not ('[' or '<' or '{') ? "." : "") + rendered;
    }

    private static string Shared(string path, IrObjectType type) =>
        $"the value at {path} is the recursive type of {type.Identity!.Object}, which every procedure that reaches it shares, so it can't be overridden for one of them.";

    private static string Describe(IrPathSegment segment) => segment switch
    {
        IrPathKey key => $"key \"{key.Key}\"",
        IrPathItems => "items ([])",
        IrPathIndex index => $"position [{index.Index}]",
        IrPathValues => "record values ({})",
        IrPathJson => "JSON content (<json>)",
        IrPathShared shared => $"<{shared.Owner}>",
        _ => "such part",
    };

    private static string Describe(IrType type) => type switch
    {
        IrRef => "an object",
        IrArray => "an array",
        IrTuple tuple => $"a tuple of {tuple.Items.Count}",
        IrRecord => "a record",
        IrUnion => "a value of several types",
        IrJsonText { Content: null } => "JSON text of unknown content",
        IrJsonText => "JSON text",
        IrImport import => $"the imported {import.Name}",
        IrUnknownType => "of unknown type",
        IrScalar scalar => $"a {scalar.Kind.ToString().ToLowerInvariant()} ({scalar.Sql})",
        _ => "a value",
    };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private sealed class PathException(string message) : Exception(message);
}
