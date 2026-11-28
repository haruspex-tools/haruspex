using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.SqlServer.Sources;

/// <summary>
/// Finds the descriptions among a model's extended properties: those under one of the config file's names, on a table, a view,
/// a procedure, a column of a table or view, or a parameter of a procedure. What can't be read for certain is passed over with a
/// note (HAR1024), never guessed
/// (docs/decisions/0128-a-column-s-description-reaches-the-fields-that-are-that-column.md).
/// </summary>
public static class DescriptionFinder
{
    /// <summary>The names read as descriptions when the config file gives none: SQL Server's own tools write this one.</summary>
    public static IReadOnlyList<string> DefaultNames { get; } = ["MS_Description"];

    /// <summary>The model with its descriptions found, and a note for each passed over.</summary>
    /// <param name="model">The model, with every extended property its source sets (<see cref="DatabaseModel.ExtendedProperties"/>).</param>
    /// <param name="names">
    /// The property names that are descriptions, compared ignoring case: the server compares them in the database's collation
    /// (observed), which a folder of scripts doesn't say, so two that differ only in case are taken as one set twice.
    /// </param>
    public static (DatabaseModel Model, IReadOnlyList<Diagnostic> Diagnostics) Find(DatabaseModel model, IReadOnlyCollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(names);
        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<Diagnostic>();
        var set = new Dictionary<Target, List<ExtendedProperty>>(TargetComparer.Instance);
        var doubted = new HashSet<Target>(TargetComparer.Instance);

        foreach (var property in model.ExtendedProperties)
        {
            // A name that is a literal and no description's is no description, whatever its other arguments.
            if (property.Name is { } name && !wanted.Contains(name))
                continue;
            var target = Of(property.Levels);
            var what = target is { } known ? $"The description of {known}" : "A description";
            if (property.Unread is { } why)
            {
                // A name that isn't a literal may be a description's: say so, since nothing is guessed.
                diagnostics.Add(SourceCodes.DescriptionNotRead.At(property.At, $"{what} is passed over: {Lower(why)}"));
                if (target is { } unread)
                    doubted.Add(unread);
                continue;
            }

            if (target is not { } on)
                continue;
            if (!Defines(model, on))
            {
                diagnostics.Add(SourceCodes.DescriptionNotRead.At(property.At, $"{what} is passed over: the source doesn't define {on.Named}."));
                continue;
            }

            if (property.Action != PropertyAction.Add)
            {
                var verb = property.Action == PropertyAction.Update ? "updated" : "dropped";
                diagnostics.Add(SourceCodes.DescriptionNotRead.At(
                    property.At,
                    $"{what} is {verb} here, and what stands depends on the order the scripts run, so it has none."));
                doubted.Add(on);
                continue;
            }

            if (!set.TryGetValue(on, out var adds))
                set[on] = adds = [];
            adds.Add(property);
        }

        var objects = new List<KeyValuePair<ObjectName, string>>();
        var parts = new List<KeyValuePair<(ObjectName, string), string>>();
        foreach (var (target, adds) in set)
        {
            if (adds.Count > 1)
            {
                // The server refuses a second add of one property (15233), and two names that differ only in case may be one.
                diagnostics.Add(SourceCodes.DescriptionNotRead.At(
                    adds[1].At,
                    $"The description of {target} is set more than once, so which stands isn't known, and it has none."));
                continue;
            }

            if (doubted.Contains(target) || adds[0].Value is not { } text || string.IsNullOrWhiteSpace(text))
                continue;

            // Half of a surrogate pair alone is no text: no output can hold it (UTF-8 can't encode it, and the IR's reader refuses
            // it), so it is passed over, as a value that isn't text is. A script is decoded with replacement and a package is XML,
            // so neither gives one today; this keeps it so whatever a source becomes.
            if (!IsText(text))
            {
                diagnostics.Add(SourceCodes.DescriptionNotRead.At(adds[0].At, $"The description of {target} is passed over: its value holds half of a UTF-16 surrogate pair alone, which is no text."));
                continue;
            }

            // With LF line endings, as a query's text is kept: a checkout may give a script CRLF, and the output must not differ.
            text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (target.Part is { } part)
                parts.Add(new((target.Object, part), text));
            else
                objects.Add(new(target.Object, text));
        }

        return (WithDescriptions(model, new Descriptions(objects, parts)), diagnostics);
    }

    private static DatabaseModel WithDescriptions(DatabaseModel model, Descriptions descriptions) =>
        new(model.Tables, model.UserTypes, model.Procedures, model.Functions, model.Views, model.Synonyms, model.Triggers, model.CascadingKeys, model.TableRedefinitions)
        {
            Settings = model.Settings,
            Unread = model.Unread,
            Databases = model.Databases,
            DefaultCollation = model.DefaultCollation,
            ExtendedProperties = model.ExtendedProperties,
            XmlSchemaCollections = model.XmlSchemaCollections,
            Sequences = model.Sequences,
            Descriptions = descriptions,
        };

    /// <summary>
    /// What a property's levels name, when it is one a description is read on; null for any other (a schema, an index, a
    /// constraint, a function), which no output has. Level types are compared ignoring case, as the server compares them (observed).
    /// </summary>
    private static Target? Of(EquatableArray<PropertyLevel> levels)
    {
        static bool Is(PropertyLevel level, string type) => level.Type.Equals(type, StringComparison.OrdinalIgnoreCase);
        if (levels.Count < 2 || !Is(levels[0], "SCHEMA"))
            return null;
        var name = new ObjectName(levels[0].Name, levels[1].Name);
        return levels switch
        {
            [_, var o] when Is(o, "TABLE") => new Target(name, null, ObjectKind.Table),
            [_, var o] when Is(o, "VIEW") => new Target(name, null, ObjectKind.View),
            [_, var o] when Is(o, "PROCEDURE") => new Target(name, null, ObjectKind.Procedure),
            [_, var o, var c] when Is(o, "TABLE") && Is(c, "COLUMN") => new Target(name, c.Name, ObjectKind.Table),
            [_, var o, var c] when Is(o, "VIEW") && Is(c, "COLUMN") => new Target(name, c.Name, ObjectKind.View),
            [_, var o, var p] when Is(o, "PROCEDURE") && Is(p, "PARAMETER") => new Target(name, p.Name, ObjectKind.Procedure),
            _ => null,
        };
    }

    /// <summary>
    /// Whether the model defines what the target names, as the server requires (15135: observed). A view's columns are known by
    /// name only where its column list gives them; elsewhere the view's being there is taken, since its query names them.
    /// </summary>
    private static bool Defines(DatabaseModel model, Target target) => target.Kind switch
    {
        ObjectKind.Table => model.FindTable(target.Object) is { } table
            && (target.Part is not { } column || table.Columns.Any(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase))),
        ObjectKind.View => model.Views.FirstOrDefault(v => v.Name.Equals(target.Object)) is { } view
            && (target.Part is not { } column || view.Columns.Count == 0 || view.Columns.Contains(column, StringComparer.OrdinalIgnoreCase)),
        _ => model.FindProcedure(target.Object) is { } procedure
            && (target.Part is not { } parameter || procedure.Parameters.Any(p => p.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase))),
    };

    /// <summary>Whether every surrogate in <paramref name="text"/> is half of a pair, so that it is text UTF-8 can hold.</summary>
    private static bool IsText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
            else if (char.IsSurrogate(text[i]))
                return false;
        }

        return true;
    }

    private static string Lower(string sentence) => sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];

    private enum ObjectKind
    {
        Table,
        View,
        Procedure,
    }

    /// <summary>What a description is on: an object, or a column or parameter of one (<see cref="Part"/>).</summary>
    private readonly record struct Target(ObjectName Object, string? Part, ObjectKind Kind)
    {
        /// <summary>The object, or its part, as a message names it.</summary>
        public string Named => Part is { } part ? $"{Object}.{part}" : Object.ToString();

        public override string ToString() => Named;
    }

    /// <summary>Targets compared as the server compares their names: ignoring case, as <see cref="ObjectName"/> is.</summary>
    private sealed class TargetComparer : IEqualityComparer<Target>
    {
        public static TargetComparer Instance { get; } = new();

        public bool Equals(Target x, Target y) =>
            x.Object.Equals(y.Object) && x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Part, y.Part);

        public int GetHashCode(Target obj) => HashCode.Combine(obj.Object, obj.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Part ?? ""));
    }
}
