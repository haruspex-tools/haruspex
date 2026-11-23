namespace Haruspex.SqlServer.Semantics;

/// <summary>
/// What a name written without a schema means (<see cref="SchemaNames.Resolve"/>): the one object of that name, several (so
/// which depends on the caller's default schema, where the caller's rule applies), or none, when it means <c>dbo</c>'s, which
/// the project doesn't have.
/// </summary>
/// <remarks>A closed hierarchy: <see cref="OneObject"/>, <see cref="SeveralObjects"/>, <see cref="NoObject"/>.</remarks>
public abstract record SchemaNameResolution
{
    private protected SchemaNameResolution()
    {
    }
}
