using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A column's JSON Schema named in the SQL, by the extended property <c>haruspex.jsonSchema</c> on a table's column, read from
/// scripts and packages alike (<see cref="DatabaseModel.ExtendedProperties"/>). It declares what <c>columns</c> in the config
/// file declares, and is recorded the same way: declared, not proven. Where both declare a column they must name the same
/// schema; a disagreement is an error, never a choice
/// (docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md).
/// </summary>
public static class ColumnAnnotations
{
    /// <summary>The extended property's name, compared ignoring case, as the server compares it in a database that ignores case (observed).</summary>
    public const string PropertyName = "haruspex.jsonSchema";

    /// <summary>
    /// The config file's declarations with the SQL's added, and the problems found. An error stops the run; a warning is an
    /// annotation haruspex doesn't read where it is (a parameter's, for now), which leaves the JSON there unknown, as it was.
    /// </summary>
    /// <param name="config">The config file, whose <c>columns</c> and folder (the schema's path is relative to it) are used.</param>
    /// <param name="model">The project, with every extended property its source sets.</param>
    /// <param name="viewSources">The table columns a view's column reads, as <see cref="ColumnDeclarationCheck"/> takes them.</param>
    public static (IReadOnlyList<ColumnDeclaration> Columns, IReadOnlyList<Diagnostic> Diagnostics) Merge(HaruspexConfig config, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(viewSources);
        var problems = new List<Diagnostic>();
        var columns = new List<ColumnDeclaration>(config.Columns);
        var files = new SchemaFiles(config.Directory, problems);
        var found = new Dictionary<Column, (ObjectName Table, List<ExtendedProperty> Adds)>(ReferenceEqualityComparer.Instance);

        foreach (var property in model.ExtendedProperties)
        {
            // A name that isn't a literal is no annotation that can be read; descriptions report it (HAR1024) if it is theirs.
            if (property.Name is not { } name || !name.Equals(PropertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Target(property, model, viewSources, problems) is var (table, column))
            {
                if (!found.TryGetValue(column, out var on))
                    found[column] = on = (table, []);
                on.Adds.Add(property);
            }
        }

        foreach (var (column, (table, adds)) in found)
        {
            var property = adds[0];
            if (adds.Count > 1)
            {
                // The server refuses a second add (15233), and two names that differ only in case may be one.
                problems.Add(ConfigCodes.AnnotationNotRead.At(adds[1].At, $"The JSON Schema of {table}.{column.Name} is named more than once, so which stands isn't known."));
                continue;
            }

            if (property.Value is not { Length: > 0 } reference || string.IsNullOrWhiteSpace(reference))
            {
                problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {table}.{column.Name} is named by no text: give the schema file's path, relative to the config file's folder."));
                continue;
            }

            var key = Key(table, column);
            if (ColumnSchema.Declare(files, config.Directory, key, table, column.Name, reference, property.At, problems) is not { } declared)
                continue;

            // The config file's declaration of the same column, found through the model, so that its spelling doesn't matter.
            var configured = config.Columns.FirstOrDefault(c => ReferenceEquals(model.FindTable(c.Table)?.Column(c.Column), column));
            if (configured is null)
            {
                columns.Add(declared);
                continue;
            }

            if (!string.Equals(configured.Record.Schema, declared.Record.Schema, StringComparison.Ordinal)
                || !string.Equals(configured.Record.Pointer, declared.Record.Pointer, StringComparison.Ordinal))
            {
                problems.Add(ConfigCodes.AnnotationDisagrees.At(
                    property.At,
                    $"{key} is declared to hold JSON of {Named(declared)} here and of {Named(configured)} in the config file{(configured.At is JsonLocation { Path: var at } ? $", at {at}" : "")}: declare it once, or name the same schema in both."));
            }
        }

        return (columns, problems);
    }

    /// <summary>
    /// The table column an annotation is on; null when it isn't on one haruspex reads it on, with the problem reported: an error
    /// where it can't be read or the column can't hold a declaration, a warning where it is on a parameter.
    /// </summary>
    private static (ObjectName Table, Column Column)? Target(ExtendedProperty property, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources, List<Diagnostic> problems)
    {
        static bool Is(PropertyLevel level, string type) => level.Type.Equals(type, StringComparison.OrdinalIgnoreCase);
        var levels = property.Levels;
        var named = string.Join(".", levels.Select(l => l.Name));
        if (property.Unread is { } why)
        {
            problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema named on {(named.Length == 0 ? "the database" : named)} is passed over: {Lower(why)}"));
            return null;
        }

        if (property.Action != PropertyAction.Add)
        {
            var verb = property.Action == PropertyAction.Update ? "updated" : "dropped";
            problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {named} is {verb} here, and what stands depends on the order the scripts run: name it once, with sp_addextendedproperty."));
            return null;
        }

        if (levels is not [var schema, var owner, ..] || !Is(schema, "SCHEMA"))
        {
            problems.Add(ConfigCodes.AnnotationNotDeclarable.At(property.At, $"The JSON Schema named on {(named.Length == 0 ? "the database" : named)} isn't read: haruspex reads one on a table's column only."));
            return null;
        }

        var name = new ObjectName(schema.Name, owner.Name);
        switch (levels)
        {
            case [_, _, _] when (Is(owner, "TABLE") || Is(owner, "VIEW")) && Is(levels[2], "COLUMN"):
                var column = levels[2].Name;
                if ((Is(owner, "TABLE") && model.FindTable(name) is null) || (Is(owner, "VIEW") && model.FindReadable(name) is not View))
                {
                    problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {name}.{column} is passed over: the source doesn't define {name}."));
                    return null;
                }

                if (Is(owner, "VIEW") && model.FindReadable(name) is View { Columns: { Count: > 0 } viewColumns } && !viewColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {name}.{column} is passed over: the view {name} has no column {column}."));
                    return null;
                }

                if (model.FindTable(name) is { } table && table.Column(column) is null)
                {
                    problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {name}.{column} is passed over: the table {name} has no column {column}."));
                    return null;
                }

                if (ColumnDeclarationCheck.Problem(name, column, model, viewSources) is { } problem)
                {
                    problems.Add(ConfigCodes.ColumnNotDeclarable.At(property.At, $"The JSON Schema of {name}.{column} can't be declared: {problem}"));
                    return null;
                }

                return model.FindTable(name) is { } held && held.Column(column) is { } found ? (held.Name, found) : null;
            case [_, _, var functionPart] when Is(owner, "FUNCTION") && Is(functionPart, "COLUMN"):
                if (model.FindFunction(name) is null)
                {
                    problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {name}.{functionPart.Name} is passed over: the source doesn't define {name}."));
                    return null;
                }

                problems.Add(ConfigCodes.ColumnNotDeclarable.At(
                    property.At,
                    $"The JSON Schema of {name}.{functionPart.Name} can't be declared: {name} is a table function, whose column holds what its query returns: declare the table column it reads instead, and the function's gets the declaration from it."));
                return null;
            case [_, _, var parameter] when (Is(owner, "PROCEDURE") || Is(owner, "FUNCTION")) && Is(parameter, "PARAMETER"):
                var defined = Is(owner, "PROCEDURE")
                    ? model.FindProcedure(name)?.Parameters.Any(p => p.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase))
                    : model.FindFunction(name)?.Parameters.Any(p => p.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase));
                if (defined is not true)
                {
                    problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {name}.{parameter.Name} is passed over: the source doesn't define {(defined is null ? name.ToString() : $"{name}.{parameter.Name}")}."));
                    return null;
                }

                problems.Add(ConfigCodes.AnnotationNotDeclarable.At(
                    property.At,
                    $"The JSON Schema of the parameter {name}.{parameter.Name} isn't read: haruspex reads one on a table's column only, so it declares nothing here."));
                return null;
            default:
                problems.Add(ConfigCodes.AnnotationNotDeclarable.At(property.At, $"The JSON Schema named on {named} isn't read: haruspex reads one on a table's column only."));
                return null;
        }
    }

    /// <summary>The column as the IR records it, <c>schema.table.column</c>, each part as the source names it, in brackets when it holds a dot or a bracket.</summary>
    private static string Key(ObjectName table, Column column) => $"{Part(table.Schema)}.{Part(table.Name)}.{Part(column.Name)}";

    private static string Part(string name) =>
        name.AsSpan().IndexOfAny('.', '[', ']') < 0 ? name : $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>A declaration's schema, as its reference names it: the file, and the pointer inside it.</summary>
    private static string Named(ColumnDeclaration declared) =>
        declared.Record.Pointer is { } pointer ? $"{declared.Record.Schema}#{pointer}" : declared.Record.Schema;

    private static string Lower(string sentence) => sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];
}
