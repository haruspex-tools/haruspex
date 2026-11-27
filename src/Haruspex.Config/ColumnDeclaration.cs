using System.Diagnostics.CodeAnalysis;
using Haruspex.Collections;
using Haruspex.Diagnostics;
using Haruspex.Ir;
using Haruspex.SqlServer.Model;

namespace Haruspex.Config;

/// <summary>
/// A column the config file declares to hold JSON of a schema (<c>columns["shop.orders.settings"].json</c>), translated into the
/// IR: what its text parses to, part of the column's type, which the inference reads at every read of the column
/// (docs/decisions/0093-a-column-s-json-is-part-of-its-type-completed-by-the-config.md).
/// </summary>
/// <param name="Table">The table, as the key names it.</param>
/// <param name="Column">The column's name, as the key writes it.</param>
/// <param name="Content">What a non-null value of the column parses to.</param>
/// <param name="Types">The object types <paramref name="Content"/> refers to.</param>
/// <param name="Record">The declaration as the IR records it: the column, the schema file and pointer, and every file's hash.</param>
/// <param name="At">Where the declaration is written: in the config file, or in the SQL (<see cref="ColumnAnnotations"/>).</param>
public sealed record ColumnDeclaration(ObjectName Table, string Column, IrType Content, EquatableMap<string, IrObjectType> Types, IrColumnDeclaration Record, DiagnosticLocation At)
{
    /// <summary>
    /// The column a key of <c>columns</c> names, as <c>schema.table.column</c>: three parts, each in brackets when it holds a dot or
    /// a bracket, as T-SQL quotes a name (<c>[sales.eu].orders.settings</c>, <c>]]</c> for a bracket inside one); or why the key
    /// is not one.
    /// </summary>
    public static bool TryParseKey(string key, out ObjectName table, [NotNullWhen(true)] out string? column, [NotNullWhen(false)] out string? problem)
    {
        table = default;
        column = null;
        ArgumentNullException.ThrowIfNull(key);
        var parts = new List<string>();
        var part = new System.Text.StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            switch (key[i])
            {
                case '[':
                    var end = i + 1;
                    for (; end < key.Length; end++)
                    {
                        if (key[end] != ']')
                            part.Append(key[end]);
                        else if (end + 1 < key.Length && key[end + 1] == ']')
                            part.Append(key[++end]);
                        else
                            break;
                    }

                    if (end >= key.Length)
                    {
                        problem = $"'{key}' has a '[' without its closing ']'.";
                        return false;
                    }

                    i = end;
                    break;
                case '.':
                    parts.Add(part.ToString());
                    part.Clear();
                    break;
                default:
                    part.Append(key[i]);
                    break;
            }
        }

        parts.Add(part.ToString());
        if (parts.Count != 3 || parts.Any(p => p.Length == 0))
        {
            problem = $"'{key}' is not a column named schema.table.column; write a part that holds a dot in brackets, as [a.b].";
            return false;
        }

        (table, column, problem) = (new ObjectName(parts[0], parts[1]), parts[2], null);
        return true;
    }
}
