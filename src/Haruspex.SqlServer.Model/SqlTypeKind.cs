namespace Haruspex.SqlServer.Model;

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
