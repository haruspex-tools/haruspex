using System.Text.Encodings.Web;
using System.Text.Json;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Inference;

/// <summary>
/// Turns inferred shapes into an <see cref="IrDocument"/>: settles a name for every procedure and object type
/// (names are decided once, here, so every emitter agrees), and lists every unknown with its path.
/// </summary>
internal sealed class IrBuilder
{
    private readonly Dictionary<string, IrObjectType> types = new(StringComparer.Ordinal);
    private readonly HashSet<string> names = new(StringComparer.Ordinal);
    private readonly List<IrUnknown> unknowns = [];

    public IrDocument Build(IReadOnlyList<(Procedure Source, ProcedureAnalysis Analysis)> procedures)
    {
        var ordered = procedures.OrderBy(p => p.Source.Name.ToString(), StringComparer.Ordinal).ToList();
        var procedureNames = ProcedureNames(ordered.Select(p => p.Source.Name).ToList());

        // Procedure names are taken first, so that a type can never take a procedure's name.
        foreach (var name in procedureNames.Values)
            names.Add(name);

        var objects = new List<IrObject>();
        foreach (var (source, analysis) in ordered)
        {
            var id = source.Name.ToString();
            var name = procedureNames[source.Name];

            var parameters = analysis.Parameters
                .Select(p => new IrParameter(p.Name, Convert(p.Type, "", id, $"parameters.{p.Name}"), p.HasDefault, p.IsOutput))
                .ToList();

            List<IrResult>? results = null;
            if (analysis.Results is { } shapes)
            {
                results = [];
                for (var i = 0; i < shapes.Count; i++)
                {
                    // The procedure's name is already reserved; a second result's base name must be reserved here.
                    var root = shapes.Count == 1 ? name : Unique($"{name}Result{i + 1}");
                    results.Add(new IrResult(shapes[i].Encoding, Root(shapes[i].Shape, root, id, $"results[{i}]"), shapes[i].AbsentBecause));
                }
            }
            else
            {
                var why = analysis.ResultsUnknown!;
                unknowns.Add(new IrUnknown(id, "results", why.Reason, why.Detail, why.At.ToString()));
            }

            var at = source.At;
            objects.Add(new IrObject(id, name, IrObjectKind.Procedure, at.ToString(), parameters, results));
        }

        return new IrDocument(new IrSource("tsql"), objects, types, unknowns);
    }

    /// <summary>
    /// A result's root type. Its object is the procedure's name plus <c>Row</c>, whether it is one of a list
    /// or the single object of <c>WITHOUT_ARRAY_WRAPPER</c>; the procedure's own name is kept for the whole
    /// result, which may be null. Nested names build on the procedure's name.
    /// </summary>
    private IrType Root(Shape shape, string name, string id, string path) => shape switch
    {
        ObjectShape obj => Object(obj, Unique(name + "Row"), name, id, path),
        ArrayShape { Items: ObjectShape row } array => Array(array, Object(row, Unique(name + "Row"), name, id, path + "[]")),
        _ => Convert(shape, name, id, path),
    };

    private IrType Convert(Shape shape, string objectName, string id, string path)
    {
        switch (shape)
        {
            case ScalarShape scalar:
                return scalar.Type;
            case JsonTextShape:
                return new IrJsonText();
            case ArrayShape array:
                return Array(array, Convert(array.Items, objectName, id, path + "[]"));
            case TupleShape tuple:
                // Each position is its own path, so an unknown element is reported where it is.
                return new IrTuple([.. tuple.Items.Select((e, i) => new IrElement(
                    Convert(e.Type, objectName, id, path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"),
                    e.NullableBecause))]);
            case RecordShape record:
                // A record's values have no key to name them by; {} stands for any key.
                return new IrRecord(Convert(record.Values, objectName, id, path + "{}"), record.ValuesNullableBecause);
            case UnionShape union:
                return new IrUnion([.. union.Variants.Select(v => Convert(v, objectName, id, path))]);
            case ObjectShape obj:
                var name = Unique(objectName);
                return Object(obj, name, name, id, path);
            case UnknownShape unknown:
                unknowns.Add(new IrUnknown(id, path, unknown.Reason, unknown.Detail, unknown.At.ToString()));
                return new IrUnknownType(unknown.Reason);
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown shape.");
        }
    }

    private static IrArray Array(ArrayShape array, IrType items) =>
        new(items) { ItemsNullableBecause = array.ItemsNullableBecause ?? [], MinItems = array.MinItems, MaxItems = array.MaxItems };

    /// <param name="name">The type's name, already reserved.</param>
    /// <param name="childBase">What nested types' names start with.</param>
    private IrRef Object(ObjectShape obj, string name, string childBase, string id, string path)
    {
        var fields = new List<IrField>();
        foreach (var field in obj.Fields)
        {
            var pascal = Naming.Pascal(field.Name);
            var childName = childBase + (field.Type is ArrayShape or RecordShape ? Naming.Singular(pascal) : pascal);
            fields.Add(new IrField(field.Name, Convert(field.Type, childName, id, path + Segment(field.Name)), field.Presence, field.NullableBecause));
        }

        types[name] = new IrObjectType(fields);
        return new IrRef(name);
    }

    private string Unique(string name)
    {
        var candidate = name;
        for (var n = 2; !names.Add(candidate); n++)
            candidate = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>
    /// Each procedure's name: its own name in Pascal case, with the schema in front when two schemas have a
    /// procedure of the same name.
    /// </summary>
    private static Dictionary<ObjectName, string> ProcedureNames(IReadOnlyList<ObjectName> procedures)
    {
        var result = new Dictionary<ObjectName, string>();
        foreach (var group in procedures.GroupBy(p => Naming.Pascal(p.Name), StringComparer.Ordinal))
        {
            foreach (var procedure in group)
                result[procedure] = group.Count() == 1 ? group.Key : Naming.Pascal(procedure.Schema) + group.Key;
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var procedure in procedures)
        {
            var name = result[procedure];
            for (var n = 2; !taken.Add(result[procedure]); n++)
                result[procedure] = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static string Segment(string key) =>
        IsIdentifier(key) ? "." + key : "[\"" + JsonEncodedText.Encode(key, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"]";

    private static bool IsIdentifier(string key) =>
        key.Length > 0 && (char.IsAsciiLetter(key[0]) || key[0] == '_') && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
