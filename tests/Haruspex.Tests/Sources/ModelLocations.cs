using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;
using Haruspex.SqlServer.Sources;

namespace Haruspex.Tests.Sources;

/// <summary>
/// Everything a read put in the model, one line for each object with what it is, its name and where it is: tables and their
/// columns, constraints and keys; types; procedures and functions with their parameters; views, synonyms and triggers;
/// foreign keys with actions; statements that change an object after its definition; and the diagnostics. Each location is
/// written as <c>file:line:column</c>, as every diagnostic and the IR write it.
/// </summary>
/// <remarks>
/// Lines are sorted, so the rendering says what was read and where, whatever order the model keeps it in. It is what
/// <see cref="ModelLocationsTests"/> holds every fixture and corpus to, so that a location that moves fails there, object
/// by object, and not only where the IR happens to show it.
/// </remarks>
internal static class ModelLocations
{
    /// <summary>The read's objects and diagnostics, one sorted line each.</summary>
    public static string Render(SchemaReadResult read) => Render(read, sorted: true);

    /// <summary>
    /// The read's objects and diagnostics, one line each: sorted, or in the model's own order, kind by kind, so that two reads
    /// can be compared for their order too.
    /// </summary>
    public static string Render(SchemaReadResult read, bool sorted)
    {
        ArgumentNullException.ThrowIfNull(read);
        var lines = new List<string>();
        var model = read.Model;
        foreach (var table in model.Tables)
        {
            lines.Add($"table {table.Name} at {table.Span}{(table.IsGraph ? ", a graph table" : "")}");
            foreach (var column in table.Columns)
                lines.Add($"column {table.Name}.{column.Name} {column.Type?.ToString() ?? "computed"} at {column.At}");
            foreach (var check in table.Checks)
                lines.Add($"check {table.Name} {check.Name ?? "unnamed"} at {check.At}{Disabled(check.DisabledAt)}");
            foreach (var key in table.UniqueKeys)
                lines.Add($"key {table.Name} {key.Name ?? "unnamed"} ({string.Join(", ", key.Columns)}) at {key.At}{Disabled(key.DisabledAt)}");
        }

        foreach (var type in model.UserTypes)
            lines.Add($"type {type.Name} {(type is AliasType alias ? alias.BaseType.ToString() : "CLR")} at {type.Span}");
        foreach (var procedure in model.Procedures)
        {
            lines.Add($"procedure {procedure.Name} at {procedure.Span}{AnsiNullsOff(procedure.AnsiNullsOff)}{Changed(procedure.Redefinition)}");
            foreach (var parameter in procedure.Parameters)
                lines.Add($"parameter {procedure.Name} {parameter.Name} at {parameter.At}");
        }

        foreach (var function in model.Functions)
        {
            lines.Add($"function {function.Name} at {function.Span}{AnsiNullsOff(function.AnsiNullsOff)}{Changed(function.Redefinition)}");
            foreach (var parameter in function.Parameters)
                lines.Add($"parameter {function.Name} {parameter.Name} at {parameter.At}");
        }

        // One for each name a FROM clause may mean besides a table: the first view, synonym or function of that name.
        foreach (var named in model.Readable)
        {
            switch (named)
            {
                case View { Redefinition: null } view:
                    lines.Add($"view {view.Name} at {view.Span}{AnsiNullsOff(view.AnsiNullsOff)}");
                    break;
                case View view:
                    lines.Add($"view {view.Name}, defined more than once");
                    break;
                case Synonym synonym:
                    lines.Add($"synonym {synonym.Name} for {string.Join(".", new[] { synonym.For.Server, synonym.For.Database, synonym.For.Schema, synonym.For.Name }.OfType<string>())}");
                    break;
                case Function:
                    lines.Add($"named function {named.Name}");
                    break;
            }
        }

        foreach (var trigger in model.Triggers)
            lines.Add($"trigger {trigger.Name} on {trigger.Parent} at {trigger.Span}{(trigger.Body is SqlBody ? "" : ", CLR")}");
        foreach (var key in model.CascadingKeys)
            lines.Add($"cascading key {key.Referencing} to {key.Referenced} at {key.At}");
        foreach (var change in model.TableRedefinitions)
            lines.Add($"table {change.Name} changed by {change.Statement} at {change.At}");
        foreach (var diagnostic in read.Diagnostics)
            lines.Add($"diagnostic {diagnostic}");

        if (sorted)
            lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines) + "\n";
    }

    private static string Disabled(SourceLocation? at) => at is null ? "" : $", disabled at {at}";

    private static string AnsiNullsOff(bool off) => off ? ", ANSI_NULLS off" : "";

    private static string Changed(Redefinition? redefinition) => redefinition is null ? "" : $", changed by {redefinition.Statement} at {redefinition.At}";
}
