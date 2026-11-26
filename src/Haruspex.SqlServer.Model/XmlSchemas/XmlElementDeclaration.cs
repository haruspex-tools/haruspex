namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>An element declaration, global or in a type's content.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its type, by its place in <see cref="XmlSchemaDeclarations.Types"/>.</param>
/// <param name="Nillable">Whether an instance may mark it <c>xsi:nil</c>.</param>
/// <param name="Abstract">Whether it is abstract, so that only a member of its substitution group appears.</param>
/// <param name="HeadsGroup">Whether another element names it as its substitution group, so that it may stand in its place.</param>
public sealed record XmlElementDeclaration(XmlName Name, int Type, bool Nillable, bool Abstract, bool HeadsGroup);
