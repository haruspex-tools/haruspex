using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A JSON Schema named in the SQL, by the extended property <c>haruspex.jsonSchema</c> on a table's column or on a procedure's or
/// function's parameter, read from scripts and packages alike (<see cref="DatabaseModel.ExtendedProperties"/>). It declares what
/// <c>columns</c> or <c>parameters</c> in the config file declares, and is recorded the same way: declared, not proven. Where both
/// declare a column or a parameter they must name the same schema; a disagreement is an error, never a choice
/// (docs/decisions/0141-a-column-s-json-schema-may-be-named-in-the-sql.md, docs/decisions/0143-a-parameter-s-json-may-be-declared.md).
/// </summary>
public static class JsonSchemaAnnotations
{
    /// <summary>The extended property's name, compared ignoring case, as the server compares it in a database that ignores case (observed).</summary>
    public const string PropertyName = "haruspex.jsonSchema";

    /// <summary>
    /// The config file's declarations with the SQL's added, and the problems found. An error stops the run; a warning is an
    /// annotation haruspex doesn't read where it is (an object's, say), which declares nothing.
    /// </summary>
    /// <param name="config">The config file, whose <c>columns</c>, <c>parameters</c> and folder (the schema's path is relative to it) are used.</param>
    /// <param name="model">The project, with every extended property its source sets.</param>
    /// <param name="viewSources">The table columns a view's column reads, as <see cref="ColumnDeclarationCheck"/> takes them.</param>
    public static (IReadOnlyList<ColumnDeclaration> Columns, IReadOnlyList<ParameterDeclaration> Parameters, IReadOnlyList<Diagnostic> Diagnostics) Merge(HaruspexConfig config, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(viewSources);
        var problems = new List<Diagnostic>();
        var columns = new List<ColumnDeclaration>(config.Columns);
        var parameters = new List<ParameterDeclaration>(config.Parameters);
        var files = new SchemaFiles(config.Directory, problems);
        var found = new Dictionary<Column, (ObjectName Table, List<ExtendedProperty> Adds)>(ReferenceEqualityComparer.Instance);
        var foundParameters = new Dictionary<Parameter, (ObjectName Owner, List<ExtendedProperty> Adds)>(ReferenceEqualityComparer.Instance);

        foreach (var property in model.ExtendedProperties)
        {
            // A name that isn't a literal is no annotation that can be read; descriptions report it (HAR1024) if it is theirs.
            if (property.Name is not { } name || !name.Equals(PropertyName, StringComparison.OrdinalIgnoreCase))
                continue;
            switch (Target(property, model, viewSources, problems))
            {
                case (ObjectName table, Column column):
                    if (!found.TryGetValue(column, out var on))
                        found[column] = on = (table, []);
                    on.Adds.Add(property);
                    break;
                case (ObjectName owner, Parameter parameter):
                    if (!foundParameters.TryGetValue(parameter, out var onParameter))
                        foundParameters[parameter] = onParameter = (owner, []);
                    onParameter.Adds.Add(property);
                    break;
            }
        }

        foreach (var (column, (table, adds)) in found)
        {
            if (Reference(adds, $"{table}.{column.Name}", problems) is not var (property, reference))
                continue;
            var key = Key(table, column);
            if (ColumnSchema.Declare(files, config.Directory, key, table, column.Name, reference, property.At, problems) is not { } declared)
                continue;

            // The config file's declaration of the same column, found through the model, so that its spelling doesn't matter.
            var configured = config.Columns.FirstOrDefault(c => ReferenceEquals(model.FindTable(c.Table)?.Column(c.Column), column));
            if (configured is null)
                columns.Add(declared);
            else if (Disagreement(key, declared.Record.Schema, declared.Record.Pointer, configured.Record.Schema, configured.Record.Pointer, configured.At, property.At) is { } disagreement)
                problems.Add(disagreement);
        }

        foreach (var (parameter, (owner, adds)) in foundParameters)
        {
            if (Reference(adds, $"the parameter {owner}.{parameter.Name}", problems) is not var (property, reference))
                continue;
            if (ColumnSchema.DeclareParameter(files, config.Directory, owner, parameter.Name, reference, property.At, problems) is not { } declared)
                continue;

            // The config file's declaration of the same parameter, found through the model, so that its spelling doesn't matter.
            var configured = config.Parameters.FirstOrDefault(p => ReferenceEquals(ParameterDeclarationCheck.Find(p.Owner, p.Parameter, model).Found, parameter));
            if (configured is null)
                parameters.Add(declared);
            else if (Disagreement(declared.Record.Parameter, declared.Record.Schema, declared.Record.Pointer, configured.Record.Schema, configured.Record.Pointer, configured.At, property.At) is { } disagreement)
                problems.Add(disagreement);
        }

        return (columns, parameters, problems);
    }

    /// <summary>
    /// The one annotation of a column or parameter, and the schema it names; null when there is more than one or it names none, with
    /// the problem reported.
    /// </summary>
    private static (ExtendedProperty Property, string Reference)? Reference(List<ExtendedProperty> adds, string of, List<Diagnostic> problems)
    {
        var property = adds[0];
        if (adds.Count > 1)
        {
            // The server refuses a second add (15233), and two names that differ only in case may be one.
            problems.Add(ConfigCodes.AnnotationNotRead.At(adds[1].At, $"The JSON Schema of {of} is named more than once, so which stands isn't known."));
            return null;
        }

        if (property.Value is not { Length: > 0 } reference || string.IsNullOrWhiteSpace(reference))
        {
            problems.Add(ConfigCodes.AnnotationNotRead.At(property.At, $"The JSON Schema of {of} is named by no text: give the schema file's path, relative to the config file's folder."));
            return null;
        }

        return (property, reference);
    }

    /// <summary>HAR4114 where the SQL and the config file name different schemas for what <paramref name="key"/> names; null when they agree.</summary>
    private static Diagnostic? Disagreement(string key, string schema, string? pointer, string configuredSchema, string? configuredPointer, DiagnosticLocation configuredAt, DiagnosticLocation at) =>
        string.Equals(configuredSchema, schema, StringComparison.Ordinal) && string.Equals(configuredPointer, pointer, StringComparison.Ordinal)
            ? null
            : ConfigCodes.AnnotationDisagrees.At(
                at,
                $"{key} is declared to hold JSON of {Named(schema, pointer)} here and of {Named(configuredSchema, configuredPointer)} in the config file{(configuredAt is JsonLocation { Path: var path } ? $", at {path}" : "")}: declare it once, or name the same schema in both.");

    /// <summary>
    /// The table column, or the procedure's or function's parameter, an annotation is on; null when it isn't on one haruspex reads it
    /// on, with the problem reported: an error where it can't be read or what it is on can't hold a declaration, a warning where it is
    /// on something else, such as an object.
    /// </summary>
    private static (ObjectName Owner, object On)? Target(ExtendedProperty property, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources, List<Diagnostic> problems)
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
            problems.Add(ConfigCodes.AnnotationNotDeclarable.At(property.At, $"The JSON Schema named on {(named.Length == 0 ? "the database" : named)} isn't read: haruspex reads one on a table's column or a procedure's or function's parameter only."));
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

                var (taking, refusal) = ParameterDeclarationCheck.Find(name, parameter.Name, model);
                if (taking is null)
                {
                    problems.Add(ConfigCodes.ParameterNotDeclarable.At(property.At, $"The JSON Schema of the parameter {name}.{parameter.Name} can't be declared: {refusal}"));
                    return null;
                }

                return (model.FindProcedure(name)?.Name ?? model.FindFunction(name)?.Name ?? name, taking);
            default:
                problems.Add(ConfigCodes.AnnotationNotDeclarable.At(property.At, $"The JSON Schema named on {named} isn't read: haruspex reads one on a table's column or a procedure's or function's parameter only."));
                return null;
        }
    }

    /// <summary>The column as the IR records it, <c>schema.table.column</c>, each part as the source names it, in brackets when it holds a dot or a bracket.</summary>
    private static string Key(ObjectName table, Column column) => $"{Part(table.Schema)}.{Part(table.Name)}.{Part(column.Name)}";

    private static string Part(string name) =>
        name.AsSpan().IndexOfAny('.', '[', ']') < 0 ? name : $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>A declaration's schema, as its reference names it: the file, and the pointer inside it.</summary>
    private static string Named(string schema, string? pointer) => pointer is not null ? $"{schema}#{pointer}" : schema;

    private static string Lower(string sentence) => sentence.Length == 0 ? sentence : char.ToLowerInvariant(sentence[0]) + sentence[1..];
}
