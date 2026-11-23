using Haruspex.Collections;

namespace Haruspex.SqlServer.Model;

/// <summary>A data type as declared: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>, <c>sysname</c>.</summary>
/// <remarks>
/// Two types are equal when they are written alike: the same kind, name, arguments in order and schema, and for a name written
/// without a schema, in a module of the same schema.
/// </remarks>
/// <param name="Kind">Whether it is built in, <c>xml</c>, or referred to by name.</param>
/// <param name="Name">
/// For a built-in type, its name in lower case as <c>sys.types</c> lists it (<c>datetime2</c>), whatever case it was written
/// in. Otherwise the name as written, without its schema.
/// </param>
/// <param name="Arguments">The arguments as written: <c>19</c> and <c>2</c> for <c>decimal(19,2)</c>, <c>MAX</c> for <c>nvarchar(MAX)</c>.</param>
/// <param name="Schema">The schema a type referred to by name was qualified with; null when it was not, or for a built-in type.</param>
/// <param name="ModuleSchema">
/// For a type referred to by name without a schema in a module's definition (its parameters, return type or body), the module's
/// schema, where SQL Server looks for the type before <c>dbo</c>, whoever calls or created the module; null for any other type,
/// whose schema the caller's or creator's default schema decides (see <c>SchemaSearch</c> in Haruspex.SqlServer.Semantics).
/// </param>
public sealed record SqlType(SqlTypeKind Kind, string Name, EquatableArray<string> Arguments, string? Schema = null, string? ModuleSchema = null)
{
    /// <summary>The type as written, with the name in canonical case: <c>decimal(19,2)</c>.</summary>
    public override string ToString() => Arguments.Count == 0 ? Name : Name + "(" + string.Join(",", Arguments) + ")";
}
