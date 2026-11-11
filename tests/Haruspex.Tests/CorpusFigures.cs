using Haruspex.Ir;

namespace Haruspex.Tests;

/// <summary>
/// How much of an IR document is typed, counted as a program reading the IR would count it: from the type table and the
/// results, never from inference's internals. <see cref="Definition"/> is the definition, as the figures page states it.
/// </summary>
/// <param name="Procedures">Procedures in the document.</param>
/// <param name="ProceduresWithNoResults">Procedures proved to return no result set.</param>
/// <param name="ProceduresWithUnlistedResults">Procedures whose result sets can't be listed (the IR's <c>results</c> is null).</param>
/// <param name="ProceduresReturningFields">Procedures with at least one field in their results.</param>
/// <param name="ProceduresFullyTyped">Of those, the ones with every field typed and no result unknown as a whole.</param>
/// <param name="Fields">Leaf fields in the type table.</param>
/// <param name="FieldsTyped">Of those, the ones with no unknown in them.</param>
/// <param name="FieldsOfJsonText">Of the typed ones, those that hold JSON text whose content isn't known.</param>
/// <param name="Results">Result sets of the procedures whose results are listed.</param>
/// <param name="ResultsUnknown">Of those, the ones unknown as a whole: no fields to count.</param>
/// <param name="Unknowns">Everything unknown, by reason, in ordinal order of the reason.</param>
internal sealed record CorpusFigures(
    int Procedures,
    int ProceduresWithNoResults,
    int ProceduresWithUnlistedResults,
    int ProceduresReturningFields,
    int ProceduresFullyTyped,
    int Fields,
    int FieldsTyped,
    int FieldsOfJsonText,
    int Results,
    int ResultsUnknown,
    IReadOnlyList<CorpusFigures.Reason> Unknowns)
{
    /// <summary>The definition of each figure, as the figures page states it, in Markdown.</summary>
    public const string Definition =
        "- **A field** is a leaf field of the IR's type table (`types`): a field whose value is not an object, that is, whose " +
        "type, looking through arrays, records and JSON text, is not a reference to another type. A field that holds a tuple or " +
        "a union is one field. Each named type is counted once, however many results share it.\n" +
        "- **A typed field** has no unknown in its type. A field holding JSON text whose content isn't known counts as typed, " +
        "since the IR claims it is a string holding JSON; how many there are is shown beside it.\n" +
        "- **A procedure returns fields** when the types its results reach have at least one field. It is **fully typed** when " +
        "every field they reach is typed and none of its results is unknown as a whole.\n" +
        "- **A result unknown as a whole** has a type with no fields to count, because its shape is unknown (or is JSON text " +
        "whose content isn't known).\n" +
        "- **Unknowns by reason** count unknown fields by the reason in their type, results unknown as a whole, procedures " +
        "whose results can't be listed, by the reason the IR's `unknowns` gives for their `results`, and parameters whose type " +
        "is unknown. A field or parameter unknown for two reasons is counted under each.";

    /// <summary>The figures for a document.</summary>
    public static CorpusFigures Count(IrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var byReason = new SortedDictionary<string, Reason>(StringComparer.Ordinal);
        void Add(string reason, int fields = 0, int results = 0, int procedures = 0, int parameters = 0)
        {
            var (_, f, r, p, a) = byReason.GetValueOrDefault(reason) ?? new Reason(reason, 0, 0, 0, 0);
            byReason[reason] = new Reason(reason, f + fields, r + results, p + procedures, a + parameters);
        }

        // The type table: each leaf field once.
        var untypedTypes = new HashSet<string>(StringComparer.Ordinal);
        int fields = 0, typed = 0, jsonText = 0;
        foreach (var (name, type) in document.Types)
        {
            foreach (var field in type.Fields.Where(f => !IsObject(f.Type)))
            {
                fields++;
                var reasons = UnknownReasons(field.Type);
                if (reasons.Count == 0)
                {
                    typed++;
                    if (IsUnknownJsonText(field.Type))
                        jsonText++;
                }
                else
                {
                    untypedTypes.Add(name);
                    foreach (var reason in reasons)
                        Add(reason, fields: 1);
                }
            }
        }

        int noResults = 0, unlisted = 0, returningFields = 0, fullyTyped = 0, results = 0, resultsUnknown = 0;
        foreach (var procedure in document.Objects)
        {
            foreach (var parameter in procedure.Parameters)
            {
                foreach (var reason in UnknownReasons(parameter.Type))
                    Add(reason, parameters: 1);
            }

            if (procedure.Results is not { } listed)
            {
                unlisted++;
                var why = document.Unknowns.Where(u => u.Object == procedure.Id && u.Path == "results").Select(u => u.Reason).Distinct();
                foreach (var reason in why)
                    Add(reason, procedures: 1);
                continue;
            }

            if (listed.Count == 0)
                noResults++;

            var wholeUnknown = false;
            foreach (var result in listed)
            {
                results++;
                if (IsObject(result.Type))
                    continue;
                var reasons = UnknownReasons(result.Type);
                if (reasons.Count == 0 && !IsUnknownJsonText(result.Type))
                    continue;
                resultsUnknown++;
                wholeUnknown = true;
                foreach (var reason in reasons.DefaultIfEmpty(IrUnknownReasons.JsonShapeUnknown))
                    Add(reason, results: 1);
            }

            var reached = Reached(document, listed.Select(r => r.Type));
            if (reached.Sum(name => document.Types[name].Fields.Count(f => !IsObject(f.Type))) == 0)
                continue;
            returningFields++;
            if (!wholeUnknown && !reached.Overlaps(untypedTypes))
                fullyTyped++;
        }

        return new CorpusFigures(
            document.Objects.Count,
            noResults,
            unlisted,
            returningFields,
            fullyTyped,
            fields,
            typed,
            jsonText,
            results,
            resultsUnknown,
            [.. byReason.Values]);
    }

    /// <summary>Whether a value is an object: a reference to a named type, through arrays, records and JSON text.</summary>
    private static bool IsObject(IrType type) => type switch
    {
        IrRef => true,
        IrArray array => IsObject(array.Items),
        IrRecord record => IsObject(record.Values),
        IrJsonText { Content: { } content } => IsObject(content),
        _ => false,
    };

    /// <summary>Whether a value is JSON text whose content isn't known, through arrays and records.</summary>
    private static bool IsUnknownJsonText(IrType type) => type switch
    {
        IrJsonText { Content: null } => true,
        IrJsonText { Content: { } content } => IsUnknownJsonText(content),
        IrArray array => IsUnknownJsonText(array.Items),
        IrRecord record => IsUnknownJsonText(record.Values),
        _ => false,
    };

    /// <summary>The reasons of the unknowns in a type, without following references (each named type is counted itself).</summary>
    private static SortedSet<string> UnknownReasons(IrType type)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        void Visit(IrType t)
        {
            switch (t)
            {
                case IrUnknownType unknown:
                    reasons.Add(unknown.Reason);
                    break;
                case IrArray array:
                    Visit(array.Items);
                    break;
                case IrRecord record:
                    Visit(record.Values);
                    break;
                case IrJsonText { Content: { } content }:
                    Visit(content);
                    break;
                case IrTuple tuple:
                    foreach (var element in tuple.Items)
                        Visit(element.Type);
                    break;
                case IrUnion union:
                    foreach (var variant in union.Variants)
                        Visit(variant);
                    break;
                default:
                    break;
            }
        }

        Visit(type);
        return reasons;
    }

    /// <summary>The named types reachable from some types, following references in fields to any depth.</summary>
    private static HashSet<string> Reached(IrDocument document, IEnumerable<IrType> roots)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<IrType>(roots);
        while (pending.TryPop(out var type))
        {
            switch (type)
            {
                case IrRef reference when reached.Add(reference.Name):
                    foreach (var field in document.Types[reference.Name].Fields)
                        pending.Push(field.Type);
                    break;
                case IrArray array:
                    pending.Push(array.Items);
                    break;
                case IrRecord record:
                    pending.Push(record.Values);
                    break;
                case IrJsonText { Content: { } content }:
                    pending.Push(content);
                    break;
                case IrTuple tuple:
                    foreach (var element in tuple.Items)
                        pending.Push(element.Type);
                    break;
                case IrUnion union:
                    foreach (var variant in union.Variants)
                        pending.Push(variant);
                    break;
                default:
                    break;
            }
        }

        return reached;
    }

    /// <summary>What is unknown for one reason.</summary>
    /// <param name="Name">The reason, one of <see cref="IrUnknownReasons"/>.</param>
    /// <param name="Fields">Fields unknown for it.</param>
    /// <param name="Results">Results unknown as a whole for it.</param>
    /// <param name="Procedures">Procedures whose results can't be listed for it.</param>
    /// <param name="Parameters">Parameters whose type is unknown for it.</param>
    internal sealed record Reason(string Name, int Fields, int Results, int Procedures, int Parameters);
}
