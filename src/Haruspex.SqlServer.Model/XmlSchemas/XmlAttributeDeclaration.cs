namespace Haruspex.SqlServer.Model.XmlSchemas;

/// <summary>An attribute a complex type declares.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its simple type, by its place in <see cref="XmlSchemaDeclarations.Types"/>.</param>
/// <param name="Use">Whether an element of the type always has it.</param>
public sealed record XmlAttributeDeclaration(XmlName Name, int Type, XmlAttributeUse Use);
