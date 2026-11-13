namespace Haruspex.SqlServer.Model;

/// <summary>A data type as declared: <c>decimal(19,2)</c>, <c>nvarchar(max)</c>, <c>sysname</c>.</summary>
/// <param name="kind">Whether it is built in, <c>xml</c>, or referred to by name.</param>
/// <param name="name">For a built-in type its name in lower case; otherwise the name as written.</param>
/// <param name="arguments">Its arguments as written, such as <c>19</c> and <c>2</c>, or <c>max</c>.</param>
/// <param name="schema">The schema a named type was qualified with, or null.</param>
public sealed class SqlType(SqlTypeKind kind, string name, IReadOnlyList<string> arguments, string? schema = null)
{
    /// <summary>Whether it is built in, <c>xml</c>, or referred to by name.</summary>
    public SqlTypeKind Kind { get; } = kind;

    /// <summary>
    /// For a built-in type, its name in lower case as <c>sys.types</c> lists it (<c>datetime2</c>), whatever case
    /// it was written in. Otherwise the name as written, without its schema.
    /// </summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>The arguments as written: <c>19</c> and <c>2</c> for <c>decimal(19,2)</c>, <c>MAX</c> for <c>nvarchar(MAX)</c>.</summary>
    public IReadOnlyList<string> Arguments { get; } = arguments ?? throw new ArgumentNullException(nameof(arguments));

    /// <summary>The schema a named type was qualified with, or null.</summary>
    public string? Schema { get; } = schema;

    /// <summary>The type as written, with the name in canonical case: <c>decimal(19,2)</c>.</summary>
    public override string ToString() => Arguments.Count == 0 ? Name : Name + "(" + string.Join(",", Arguments) + ")";
}
