using Haruspex.Diagnostics;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// Checks each column declared to hold JSON against the project: it must be a table's column, not computed, of a string type
/// (<c>char</c>, <c>varchar</c>, <c>nchar</c>, <c>nvarchar</c> or <c>json</c>, directly or through an alias type). A view's
/// column holds what it reads, so it gets a declaration from the table column it reads, and declaring it is an error that names
/// that column (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
public static class ColumnDeclarationCheck
{
    private static readonly HashSet<string> Strings = new(StringComparer.OrdinalIgnoreCase) { "char", "varchar", "nchar", "nvarchar", "json", "sysname" };

    /// <summary>A problem for each declaration the project can't hold; none when all can.</summary>
    /// <param name="columns">The declarations.</param>
    /// <param name="model">The project.</param>
    /// <param name="viewSources">The table columns a view's column reads, as <c>schema.table.column</c>, from the inference.</param>
    public static IReadOnlyList<Diagnostic> Check(IEnumerable<ColumnDeclaration> columns, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(viewSources);
        var problems = new List<Diagnostic>();
        foreach (var declared in columns)
        {
            if (Problem(declared, model, viewSources) is { } problem)
                problems.Add(ConfigCodes.ColumnNotDeclarable.At(declared.At, problem));
        }

        return problems;
    }

    private static string? Problem(ColumnDeclaration declared, DatabaseModel model, Func<ObjectName, string, IReadOnlyList<string>> viewSources)
    {
        switch ((SchemaObject?)model.FindTable(declared.Table) ?? model.FindReadable(declared.Table))
        {
            case View view:
                var sources = viewSources(view.Name, declared.Column);
                return sources.Count == 0
                    ? $"{declared.Table} is a view, whose column holds what it reads: declare the table column it reads instead, and the view's gets the declaration from it."
                    : $"{declared.Table} is a view, whose column holds what it reads: declare {string.Join(" and ", sources)} instead, and the view's gets the declaration from it.";
            case Table table when table.Column(declared.Column) is null:
                return $"the table {declared.Table} has no column {declared.Column}.";
            case Table table when table.Column(declared.Column) is { IsComputed: true }:
                return $"{declared.Table}.{declared.Column} is a computed column, which holds what its expression gives: declare the column it reads instead.";
            case Table table when table.Column(declared.Column)?.Type is { } type && !IsString(type, model):
                return $"{declared.Table}.{declared.Column} is of the type {type}, which can't hold JSON text: only a string type (char, varchar, nchar, nvarchar) or json can.";
            case Table:
                return null;
            default:
                return $"there is no table {declared.Table} in the project.";
        }
    }

    /// <summary>Whether a type holds text: a string type or json, or an alias type of one.</summary>
    private static bool IsString(SqlType type, DatabaseModel model) => type.Kind switch
    {
        SqlTypeKind.Named when Strings.Contains(type.Name) && type.Schema is null => true,
        SqlTypeKind.Named => model.FindUserType(new ObjectName(type.Schema ?? "dbo", type.Name)) is AliasType alias && IsString(alias.BaseType, model),
        _ => Strings.Contains(type.Name),
    };
}
