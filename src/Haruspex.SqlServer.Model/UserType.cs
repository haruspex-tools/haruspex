using Haruspex.Diagnostics;

namespace Haruspex.SqlServer.Model;

/// <summary>
/// A user-defined type: an <see cref="AliasType"/>, a built-in type under a name of its own (<c>CREATE TYPE … FROM</c>); a
/// <see cref="ClrType"/>, whose values come from .NET code (<c>CREATE TYPE … EXTERNAL NAME</c>); or a <see cref="TableType"/>,
/// the columns a table-valued parameter or table variable takes (<c>CREATE TYPE … AS TABLE</c>). The three kinds are all there
/// are (decision 0031), and share one namespace in a schema, as <c>sys.types</c> lists them.
/// </summary>
public abstract record UserType : SchemaObject
{
    /// <summary>Creates a type; only the kinds in this assembly can.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="span">Where its declaration is.</param>
    private protected UserType(ObjectName name, SourceSpan span)
        : base(name, span)
    {
    }
}
