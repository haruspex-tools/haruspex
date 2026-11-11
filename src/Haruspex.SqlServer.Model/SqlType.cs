using Microsoft.SqlServer.TransactSql.ScriptDom;

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

    /// <summary>The type from a ScriptDom data type reference, or null when there is none or it has no name.</summary>
    public static SqlType? From(DataTypeReference? type)
    {
        switch (type)
        {
            case SqlDataTypeReference sql:
#pragma warning disable CA1308 // A canonical name, as sys.types gives it, not a comparison.
                return new SqlType(SqlTypeKind.BuiltIn, sql.SqlDataTypeOption.ToString().ToLowerInvariant(), [.. sql.Parameters.Select(p => p.Value)]);
#pragma warning restore CA1308
            case XmlDataTypeReference { Name.BaseIdentifier.Value: { } xml }:
                return new SqlType(SqlTypeKind.Xml, xml, []);
            case XmlDataTypeReference:
                return null;
            case { Name.BaseIdentifier.Value: { } name }:
                var arguments = (type as ParameterizedDataTypeReference)?.Parameters.Select(p => p.Value) ?? [];
                return new SqlType(SqlTypeKind.Named, name, [.. arguments], type.Name.SchemaIdentifier?.Value);
            default:
                return null;
        }
    }

    /// <summary>The type as written, with the name in canonical case: <c>decimal(19,2)</c>.</summary>
    public override string ToString() => Arguments.Count == 0 ? Name : Name + "(" + string.Join(",", Arguments) + ")";
}

/// <summary>How a type is referred to.</summary>
public enum SqlTypeKind
{
    /// <summary>A built-in type with its own keyword, such as <c>int</c> or <c>nvarchar</c>.</summary>
    BuiltIn,

    /// <summary><c>xml</c>, possibly typed by a schema collection.</summary>
    Xml,

    /// <summary>
    /// A type referred to by name: an alias type such as <c>sysname</c>, or a CLR type such as <c>geography</c>.
    /// Which it is needs the database's catalogue, which a source of scripts does not have.
    /// </summary>
    Named,
}
