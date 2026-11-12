using Haruspex.Ir;

namespace Haruspex.Tests.Corpus;

/// <summary>
/// How much of an IR document is typed, counted as a program reading the IR would count it: from the type table and the
/// results, never from inference's internals. <see cref="Definition"/> is the definition, as the figures page states it.
/// </summary>
/// <param name="Procedures">Procedures in the document.</param>
/// <param name="ProceduresWithNoResults">Procedures proved to return no result set.</param>
/// <param name="ProceduresWithUnlistedResults">Procedures whose result sets can't be listed (the IR's <c>results</c> is null).</param>
/// <param name="ProceduresWithUnknownResults">
/// Procedures whose results are listed, but have no fields to count because a result's shape is unknown.
/// </param>
/// <param name="ProceduresWithoutFields">Procedures whose results are known and have no fields (a single value, say).</param>
/// <param name="ProceduresReturningFields">Procedures with at least one field in their results.</param>
/// <param name="ProceduresFullyTyped">Of those, the ones with every field typed and no result of unknown shape.</param>
/// <param name="Fields">Leaf fields in the type table.</param>
/// <param name="FieldsTyped">Of those, the ones with no unknown in them.</param>
/// <param name="FieldsOfJsonText">Of the typed ones, those that hold JSON text whose content isn't known.</param>
/// <param name="Results">Result sets of the procedures whose results are listed.</param>
/// <param name="ResultsUnknown">Of those, the ones whose shape is unknown, outside the named types they refer to.</param>
/// <param name="Unknowns">Everything unknown, by reason, in ordinal order of the reason.</param>
internal sealed record CorpusFigures(
    int Procedures,
    int ProceduresWithNoResults,
    int ProceduresWithUnlistedResults,
    int ProceduresWithUnknownResults,
    int ProceduresWithoutFields,
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
        "type, looking through arrays, records, JSON text, tuples and unions, reaches no reference to another type. The fields " +
        "of the types it would reach are counted in those types. A field that holds an object and also something unknown (a " +
        "union of a reference and an unknown, say) is counted as one unknown field, so that no unknown goes uncounted. Each " +
        "named type is counted once, however many results share it.\n" +
        "- **A typed field** has no unknown in its type. A field holding JSON text whose content isn't known counts as typed, " +
        "since the IR claims it is a string holding JSON; how many there are is shown beside it.\n" +
        "- **A result of unknown shape** has an unknown in its type outside the named types it refers to, or is JSON text " +
        "whose content isn't known. A result whose whole shape is unknown has no fields, so it adds nothing to the fields " +
        "counted: the procedures it leaves with no fields are counted apart, and the headline says how many procedures are " +
        "not described at all.\n" +
        "- **The procedures** fall into five rows that add up to all of them: those that return fields, those whose results " +
        "have no fields because a result's shape is unknown, those whose results can't be listed (the IR's `results` is null), " +
        "those that return no result set, and those whose known results have no fields. **Not described** are the second and " +
        "third. A procedure that returns fields is **fully typed** when every field its results reach is typed and none of its " +
        "results is of unknown shape.\n" +
        "- **Unknowns by reason** count unknown fields by the reason in their type, results of unknown shape, procedures " +
        "whose results can't be listed, by the reason the IR's `unknowns` gives for their `results`, and parameters whose type " +
        "is unknown. A field or parameter unknown for two reasons is counted under each.";

    /// <summary>Procedures haruspex can't describe at all: results that can't be listed, or with no known shape.</summary>
    public int ProceduresNotDescribed => ProceduresWithUnlistedResults + ProceduresWithUnknownResults;

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
            foreach (var field in type.Fields.Where(f => IsField(f.Type)))
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

        int noResults = 0, unlisted = 0, unknownResults = 0, withoutFields = 0, returningFields = 0, fullyTyped = 0, results = 0, resultsUnknown = 0;
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
                var why = document.Unknowns.Where(u => u.Object == procedure.Id && u.Path == "results").Select(u => IrWireNames.Of(u.Reason)).Distinct();
                foreach (var reason in why)
                    Add(reason, procedures: 1);
                continue;
            }

            if (listed.Count == 0)
            {
                noResults++;
                continue;
            }

            var unknownShape = false;
            foreach (var result in listed)
            {
                results++;
                var reasons = UnknownReasons(result.Type);
                if (reasons.Count == 0 && (HoldsObject(result.Type) || !IsUnknownJsonText(result.Type)))
                    continue;
                resultsUnknown++;
                unknownShape = true;
                foreach (var reason in reasons.DefaultIfEmpty(IrWireNames.Of(IrUnknownReason.JsonShapeUnknown)))
                    Add(reason, results: 1);
            }

            var reached = Reached(document, listed.Select(r => r.Type));
            if (reached.Sum(name => document.Types[name].Fields.Count(f => IsField(f.Type))) > 0)
            {
                returningFields++;
                if (!unknownShape && !reached.Overlaps(untypedTypes))
                    fullyTyped++;
            }
            else if (unknownShape)
            {
                unknownResults++;
            }
            else
            {
                withoutFields++;
            }
        }

        return new CorpusFigures(
            document.Objects.Count,
            noResults,
            unlisted,
            unknownResults,
            withoutFields,
            returningFields,
            fullyTyped,
            fields,
            typed,
            jsonText,
            results,
            resultsUnknown,
            [.. byReason.Values]);
    }

    /// <summary>Whether a field's value counts as a field: it reaches no named type, or it has an unknown of its own.</summary>
    private static bool IsField(IrType type) => !HoldsObject(type) || UnknownReasons(type).Count > 0;

    /// <summary>
    /// Whether a value holds an object: a reference to a named type, through arrays, records, JSON text, tuples and unions.
    /// </summary>
    private static bool HoldsObject(IrType type) => type switch
    {
        IrRef => true,
        IrArray array => HoldsObject(array.Items),
        IrRecord record => HoldsObject(record.Values),
        IrJsonText { Content: { } content } => HoldsObject(content),
        IrTuple tuple => tuple.Items.Any(e => HoldsObject(e.Type)),
        IrUnion union => union.Variants.Any(HoldsObject),
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
                    reasons.Add(IrWireNames.Of(unknown.Reason));
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
    /// <param name="Name">The reason, as the IR names it (<see cref="IrWireNames"/>).</param>
    /// <param name="Fields">Fields unknown for it.</param>
    /// <param name="Results">Results unknown as a whole for it.</param>
    /// <param name="Procedures">Procedures whose results can't be listed for it.</param>
    /// <param name="Parameters">Parameters whose type is unknown for it.</param>
    internal sealed record Reason(string Name, int Fields, int Results, int Procedures, int Parameters);
}
