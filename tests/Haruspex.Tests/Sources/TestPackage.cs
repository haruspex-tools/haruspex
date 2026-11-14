using System.IO.Compression;
using System.Text;

namespace Haruspex.Tests.Sources;

/// <summary>
/// A DACPAC a test writes for itself: a <c>model.xml</c> holding the elements given, and the database references given in its
/// header, zipped as a package is. For a case no built package in <c>tests/dacpacs</c> shows, or one built packages can't (a
/// reference named one way and a body another); the elements are written in the shapes DacFx writes them, which those packages
/// show.
/// </summary>
internal static class TestPackage
{
    /// <summary>A reference in a package's header: the package's file name, and the database when it is another.</summary>
    /// <param name="Package">The referenced package's file name, as its <c>LogicalName</c> and the end of its recorded path.</param>
    /// <param name="Database">Null for the same database; otherwise the database as <c>ExternalParts</c> names it, without its brackets (<c>$(Ledger)</c>, <c>master</c>).</param>
    public sealed record Reference(string Package, string? Database = null);

    /// <summary>Writes the package to <paramref name="path"/>.</summary>
    /// <param name="path">Where.</param>
    /// <param name="elements">The <c>Element</c>s of its model, as XML.</param>
    /// <param name="collation">The database's default collation, in its <c>SqlDatabaseOptions</c>; none when null.</param>
    /// <param name="references">Its database references.</param>
    public static void Write(string path, string elements, string? collation = "SQL_Latin1_General_CP1_CI_AS", params Reference[] references)
    {
        var header = string.Concat(references.Select(r =>
            $"""<CustomData Category="Reference" Type="SqlSchema"><Metadata Name="FileName" Value="C:\build\{r.Package}" /><Metadata Name="LogicalName" Value="{r.Package}" />{(r.Database is null ? "" : $"""<Metadata Name="ExternalParts" Value="[{r.Database}]" />""")}</CustomData>"""));
        var options = collation is null ? "" : $"""<Element Type="SqlDatabaseOptions"><Property Name="Collation" Value="{collation}" /></Element>""";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("model.xml").Open(), new UTF8Encoding(false));
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <DataSchemaModel DspName="Microsoft.Data.Tools.Schema.Sql.Sql170DatabaseSchemaProvider" xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
              <Header>{header}</Header>
              <Model>{options}{elements}</Model>
            </DataSchemaModel>
            """);
    }

    /// <summary>A table's element: its columns, each <c>name type</c> with <c>?</c> after the type when nullable (<c>"id int"</c>, <c>"note varchar(20)?"</c>).</summary>
    public static string Table(string schema, string name, params string[] columns) =>
        $"""<Element Type="SqlTable" Name="[{schema}].[{name}]"><Relationship Name="Columns">{string.Concat(columns.Select(c => Column(schema, name, c)))}</Relationship></Element>""";

    /// <summary>A procedure's element, its header and body as written.</summary>
    public static string Procedure(string schema, string name, string body) =>
        $"""<Element Type="SqlProcedure" Name="[{schema}].[{name}]"><Property Name="BodyScript"><Value><![CDATA[{"\n"}{body}]]></Value></Property><Property Name="IsAnsiNullsOn" Value="True" /><Annotation Type="SysCommentsObjectAnnotation"><Property Name="HeaderContents" Value="CREATE PROCEDURE {schema}.{name} AS" /></Annotation></Element>""";

    /// <summary>A view's element, its header and query as written.</summary>
    public static string View(string schema, string name, string query) =>
        $"""<Element Type="SqlView" Name="[{schema}].[{name}]"><Property Name="QueryScript"><Value><![CDATA[{"\n"}{query}]]></Value></Property><Property Name="IsAnsiNullsOn" Value="True" /><Annotation Type="SysCommentsObjectAnnotation"><Property Name="HeaderContents" Value="CREATE VIEW {schema}.{name} AS" /></Annotation></Element>""";

    /// <summary>A synonym's element, standing for the name written.</summary>
    public static string Synonym(string schema, string name, string target) =>
        $"""<Element Type="SqlSynonym" Name="[{schema}].[{name}]"><Property Name="ForObjectScript"><Value><![CDATA[{target}]]></Value></Property></Element>""";

    /// <summary>An alias type's element: <c>CREATE TYPE schema.name FROM base</c>, NOT NULL when <paramref name="notNull"/>.</summary>
    public static string AliasType(string schema, string name, string baseType, int? length = null, bool notNull = false) =>
        $"""<Element Type="SqlUserDefinedDataType" Name="[{schema}].[{name}]">{(notNull ? """<Property Name="IsNullable" Value="False" />""" : "")}{(length is { } l ? $"""<Property Name="Length" Value="{l}" />""" : "")}<Relationship Name="Type"><Entry><References ExternalSource="BuiltIns" Name="[{baseType}]" /></Entry></Relationship></Element>""";

    private static string Column(string schema, string table, string column)
    {
        var parts = column.Split(' ');
        var nullable = parts[1].EndsWith('?');
        var type = parts[1].TrimEnd('?');
        var length = type.Contains('(', StringComparison.Ordinal) ? type[(type.IndexOf('(', StringComparison.Ordinal) + 1)..^1] : null;
        var name = length is null ? type : type[..type.IndexOf('(', StringComparison.Ordinal)];
        var reference = name.Contains('.', StringComparison.Ordinal)
            ? $"""<References Name="[{name.Replace(".", "].[", StringComparison.Ordinal)}]" />"""
            : $"""<References ExternalSource="BuiltIns" Name="[{name}]" />""";
        return $"""<Entry><Element Type="SqlSimpleColumn" Name="[{schema}].[{table}].[{parts[0]}]">{(nullable ? "" : """<Property Name="IsNullable" Value="False" />""")}<Relationship Name="TypeSpecifier"><Entry><Element Type="SqlTypeSpecifier">{(length is null ? "" : $"""<Property Name="Length" Value="{length}" />""")}<Relationship Name="Type"><Entry>{reference}</Entry></Relationship></Element></Entry></Relationship></Element></Entry>""";
    }
}
