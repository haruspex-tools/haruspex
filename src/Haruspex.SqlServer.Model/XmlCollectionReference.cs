namespace Haruspex.SqlServer.Model;

/// <summary>
/// The schema collection an <c>xml</c> type names (<c>xml(DOCUMENT sales.spec_schema)</c>), as written: SQL Server finds a name
/// written without a schema as it finds an alias type's (observed: <c>An_unqualified_xml_schema_collection_is_found_as_an_alias_type_is</c>).
/// </summary>
/// <param name="Name">The collection's name, without its schema.</param>
/// <param name="Schema">The schema it was qualified with; null when it was not.</param>
/// <param name="Document">Whether the type is <c>DOCUMENT</c>, one root element; <c>CONTENT</c>, any number, is the default.</param>
public sealed record XmlCollectionReference(string Name, string? Schema, bool Document)
{
    /// <summary>The reference as written in a type: <c>DOCUMENT sales.spec_schema</c>, <c>spec_schema</c>.</summary>
    public override string ToString() => (Document ? "DOCUMENT " : "") + (Schema is null ? Name : Schema + "." + Name);
}
