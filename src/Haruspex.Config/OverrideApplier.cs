using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;

namespace Haruspex.Config;

/// <summary>
/// Applies a procedure's path overrides to the IR, as transformations of the document inference produced, never by
/// reaching back into inference (docs/architecture.md, "Why these lines"): they work the same however the
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
    Dictionary<string, IrObjectType> types,
    List<IrUnknown> unknowns,
    List<IrOverride> claims,
    List<Diagnostic> problems,
    Func<string, string> importFrom)
{
    /// <summary>The names given by path overrides, by object and canonical path, for the namer.</summary>
    public Dictionary<(string Object, string Path), (string Name, JsonLocation At)> Names { get; } = [];

    /// <summary>
    /// Applies one override to an object's results, returning them changed; when its path leads nowhere, or says what can't
    /// be said of the value there, the results unchanged and the problem reported.
    /// </summary>
    public EquatableArray<IrResult>? Apply(IrObject obj, EquatableArray<IrResult>? results, PathOverride entry)
    {
        // The reader has checked the path's syntax already.
        if (!IrPath.TryParse(entry.Path, out var written, out _))
            return results;
        var applied = Applied(obj, results, written, entry);
        problems.AddRange(applied.Diagnostics);
        return applied switch
        {
            Succeeded<EquatableArray<IrResult>> changed => changed.Value,
            Failed<EquatableArray<IrResult>> => results,
        };
    }

    /// <summary>The results with the override applied at its path, or why it can't be.</summary>
    private Outcome<EquatableArray<IrResult>> Applied(IrObject obj, EquatableArray<IrResult>? results, IReadOnlyList<IrPathSegment> written, PathOverride entry)
    {
        if (results is null)
            return Refused<EquatableArray<IrResult>>(ConfigCodes.NoSuchPath, entry, $"the results of {obj.Id} are unknown (see the unknowns in the IR), so no path in them exists.");

        // A path is relative to the only result, or names one: results[1][].id.
        int index;
        IReadOnlyList<IrPathSegment> rest;
        if (written is [IrPathKey { Key: "results" }, IrPathIndex given, ..])
        {
            if (given.Index >= results.Count)
                return Refused<EquatableArray<IrResult>>(ConfigCodes.NoSuchPath, entry, $"{obj.Id} returns {Count(results.Count, "result set")}, so there is no results[{given.Index}].");
            (index, rest) = (given.Index, written.Skip(2).ToList());
        }
        else if (results.Count == 1)
        {
            (index, rest) = (0, written);
        }
        else
        {
            return Refused<EquatableArray<IrResult>>(ConfigCodes.NoSuchPath, entry, $"{obj.Id} returns {Count(results.Count, "result set")}, so the path must start with the one it means, as results[0].");
        }

        var canonical = IrPath.Render([new IrPathKey("results"), new IrPathIndex(index), .. rest]);
        var result = results[index];
        var slot = new Slot(result.Type, result.AbsentBecause, CanBeNull: true);
        return Edit(slot, rest, 0, canonical, s => Settle(obj.Id, canonical, s, entry), entry).Select(changed =>
        {
            var list = results.ToList();
            list[index] = result with { Type = changed.Type, AbsentBecause = changed.Reasons };
            return (EquatableArray<IrResult>)[.. list];
        });
    }

    /// <summary>A place a value is: its type, why it can be null, and whether it can be null at all.</summary>
    private sealed record Slot(IrType Type, EquatableArray<IrNullReason> Reasons, bool CanBeNull);

    /// <summary>What the override says about the value at its path, with what it found; or why it can't say it.</summary>
    private Outcome<Slot> Settle(string objectId, string path, Slot slot, PathOverride entry)
    {
        var at = entry.At;
        var found = new List<Diagnostic>();
        if (entry.NotNull)
        {
            if (!slot.CanBeNull)
                return Refused<Slot>(ConfigCodes.NullableJsonContent, entry, $"the value at {path} is what JSON text holds, which is never null itself; nothing can be declared about its nullability.");
            if (slot.Reasons.Count == 0)
            {
                found.Add(ConfigCodes.NullableAgrees.At(at, $"\"nullable\": false agrees with the inference, which already proves the value at {path} is never null. Remove it."));
            }
            else
            {
                slot = slot with { Reasons = [] };
                claims.Add(new IrOverride(objectId, path, IrOverrideSetting.NotNull, entry.Why));
            }
        }

        if (entry.ExactValues)
        {
            if (slot.Type is not IrScalar { Facets: { Values: { } set } facets } scalar)
            {
                return Refused<Slot>(ConfigCodes.NoValueSet, entry, $"the value at {path} has no value set to declare exact. A value set is inferred from a CHECK constraint on a column the value passes through unchanged; "
                    + "canonical value sets for other cases are planned, not done.", found);
            }

            if (set.Match == IrValueMatch.Exact)
            {
                found.Add(ConfigCodes.ExactAgrees.At(at, $"\"values\": \"exact\" agrees with the inference, which already proves the set at {path} exact. Remove it."));
            }
            else
            {
                slot = slot with { Type = scalar with { Facets = facets with { Values = set with { Match = IrValueMatch.Exact } } } };
                claims.Add(new IrOverride(objectId, path, IrOverrideSetting.ExactValues, entry.Why));
            }
        }

        if (entry.Type is { } type)
        {
            // Never null: the reader refuses a "type" without its "import" (ConfigReader).
            IrType imported = new IrImport(type.TrimEnd('[', ']'), importFrom(entry.Import!));
            for (var depth = (type.Length - type.TrimEnd('[', ']').Length) / 2; depth > 0; depth--)
                imported = new IrArray(imported);
            slot = slot with { Type = imported };
            claims.Add(new IrOverride(objectId, path, IrOverrideSetting.Type, entry.Why));

            // What was unknown at or under the path is now declared.
            unknowns.RemoveAll(u => u.Object == objectId && (u.Path == path || (u.Path.StartsWith(path, StringComparison.Ordinal) && u.Path[path.Length] is '.' or '[' or '{' or '<')));
        }

        if (entry.Name is { } name)
        {
            if (slot.Type is not IrRef reference)
            {
                var hint = slot.Type is IrArray { Items: IrRef } ? $" To name its items, use the path \"{entry.Path}[]\"." : "";
                return Refused<Slot>(ConfigCodes.NameNotObject, entry, $"only an object type can be named, and the value at {path} is {Describe(slot.Type)}.{hint}", found);
            }

            if (types[reference.Name].Identity?.Owner == IrTypeOwner.Function)
                return Refused<Slot>(ConfigCodes.SharedType, entry, Shared(path, types[reference.Name]), found);
            Names[(objectId, path)] = (name, at);
        }

        return new Succeeded<Slot>(slot, [.. found]);
    }

    /// <summary>Follows the path from <paramref name="slot"/>, editing the value at its end; or why the path leads nowhere.</summary>
    private Outcome<Slot> Edit(Slot slot, IReadOnlyList<IrPathSegment> path, int i, string whole, Func<Slot, Outcome<Slot>> settle, PathOverride entry)
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
                    return Refused<Slot>(ConfigCodes.SharedType, entry, Shared(whole, obj));
                var position = obj.Fields.ToList().FindIndex(f => f.Name == key.Key);
                if (position < 0)
                {
                    return Refused<Slot>(ConfigCodes.NoSuchPath, entry, $"the path {whole} does not exist: the object at {here} has no key \"{key.Key}\". Its keys are "
                        + string.Join(", ", obj.Fields.Select(f => $"\"{f.Name}\"")) + ".");
                }

                var field = obj.Fields[position];
                return Edit(new Slot(field.Type, field.NullableBecause, CanBeNull: true), path, i + 1, whole, settle, entry).Select(edited =>
                {
                    var fields = obj.Fields.ToList();
                    fields[position] = field with { Type = edited.Type, NullableBecause = edited.Reasons };
                    types[reference.Name] = obj with { Fields = [.. fields] };
                    return slot;
                });
            case (IrPathItems, IrArray array):
                return Edit(new Slot(array.Items, array.ItemsNullableBecause, CanBeNull: true), path, i + 1, whole, settle, entry)
                    .Select(items => slot with { Type = array with { Items = items.Type, ItemsNullableBecause = items.Reasons } });
            case (IrPathIndex index, IrTuple tuple) when index.Index < tuple.Items.Count:
                var element = tuple.Items[index.Index];
                return Edit(new Slot(element.Type, element.NullableBecause, CanBeNull: true), path, i + 1, whole, settle, entry).Select(changed =>
                {
                    var elements = tuple.Items.ToList();
                    elements[index.Index] = new IrElement(changed.Type, changed.Reasons);
                    return slot with { Type = new IrTuple([.. elements]) };
                });
            case (IrPathValues, IrRecord record):
                return Edit(new Slot(record.Values, record.ValuesNullableBecause, CanBeNull: true), path, i + 1, whole, settle, entry)
                    .Select(values => slot with { Type = new IrRecord(values.Type, values.Reasons) });
            case (IrPathJson, IrJsonText { Content: { } content }):
                return Edit(new Slot(content, [], CanBeNull: false), path, i + 1, whole, settle, entry)
                    .Select(parsed => slot with { Type = new IrJsonText(parsed.Type) });
            case (_, IrUnion):
                return Refused<Slot>(ConfigCodes.PathThroughUnion, entry, $"the path {whole} passes through a value of several types at {here}, and a path can't choose between them.");
            default:
                return Refused<Slot>(ConfigCodes.NoSuchPath, entry, $"the path {whole} does not exist: the value at {here} is {Describe(slot.Type)}, so it has no {Describe(segment)}.");
        }
    }

    /// <summary>The override refused, at where it is written, after what was <paramref name="found"/> before.</summary>
    private static Failed<T> Refused<T>(DiagnosticCode code, PathOverride entry, string message, IEnumerable<Diagnostic>? found = null) =>
        new([.. found ?? [], code.At(entry.At, message)]);

    private static string Here(string whole, IReadOnlyList<IrPathSegment> path, int i)
    {
        // The canonical path is results[n] followed by the segments; the part before segment i is where the walk has reached.
        var rendered = IrPath.Render(path.Take(i));
        var prefix = whole[..(whole.Length - IrPath.Render(path).Length)];
        return prefix + (rendered.Length > 0 && rendered[0] is not ('[' or '<' or '{') ? "." : "") + rendered;
    }

    private static string Shared(string path, IrObjectType type) =>
        // Never null: a recursive type is a function's node, made with its identity (IrBuilder).
        $"the value at {path} is the recursive type of {type.Identity!.Object}, which every procedure that reaches it shares, so it can't be overridden for one of them.";

    private static string Describe(IrPathSegment segment) => segment switch
    {
        IrPathKey key => $"key \"{key.Key}\"",
        IrPathItems => "items ([])",
        IrPathIndex index => $"position [{index.Index}]",
        IrPathValues => "record values ({})",
        IrPathJson => "JSON content (<json>)",
        IrPathShared shared => $"<{shared.Owner}>",
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
#pragma warning disable CA1308 // Lower case to write the kind in a message, not to compare it.
        IrScalar { Sql: { } sql } scalar => $"a {scalar.Kind.ToString().ToLowerInvariant()} ({sql})",
        IrScalar scalar => $"a {scalar.Kind.ToString().ToLowerInvariant()}",
#pragma warning restore CA1308
    };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
