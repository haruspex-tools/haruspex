using System.Xml.Linq;

namespace Haruspex.SqlServer.Sources.Dacpacs;

/// <summary>
/// The words of a DACPAC's <c>model.xml</c> that the reader reads, in one place, and the few ways an element is read: a
/// property's value, the elements and names a relationship holds, an annotation. <c>model.xml</c> has no published
/// specification, so every name here was read from packages DacFx made (<c>tests/dacpacs/README.md</c>), and a test holds
/// the documentation's mentions of them to this list.
/// </summary>
/// <remarks>
/// An element is <c>&lt;Element Type="SqlTable" Name="[sales].[order]"&gt;</c>; its facts are <c>Property</c> elements (a
/// <c>Value</c> attribute, or a <c>Value</c> element holding text such as a body), its links to other elements are
/// <c>Relationship</c> elements whose entries hold an element or a <c>References</c> to one by name, and what DacFx keeps beside
/// it is an <c>Annotation</c>. A boolean property is written only when it is true (observed: a project with <c>AnsiWarnings</c>
/// false has no <c>IsAnsiWarningsOn</c>), so its absence is false; the exceptions read here are <see cref="IsQuotedIdentifierOn"/>
/// and <see cref="IsAnsiPaddingOn"/>, written false and absent when true, and <see cref="IsPersistedNullable"/>, absent when
/// nothing was declared.
/// </remarks>
internal static class ModelXml
{
    /// <summary>The namespace every element of <c>model.xml</c>, <c>Origin.xml</c> and <c>DacMetadata.xml</c> is in.</summary>
    public static readonly XNamespace Namespace = "http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02";

    // Element types read.
    public const string SqlTable = nameof(SqlTable);
    public const string SqlSimpleColumn = nameof(SqlSimpleColumn);
    public const string SqlComputedColumn = nameof(SqlComputedColumn);
    public const string SqlCheckConstraint = nameof(SqlCheckConstraint);
    public const string SqlPrimaryKeyConstraint = nameof(SqlPrimaryKeyConstraint);
    public const string SqlUniqueConstraint = nameof(SqlUniqueConstraint);
    public const string SqlIndex = nameof(SqlIndex);
    public const string SqlForeignKeyConstraint = nameof(SqlForeignKeyConstraint);
    public const string SqlUserDefinedDataType = nameof(SqlUserDefinedDataType);
    public const string SqlUserDefinedType = nameof(SqlUserDefinedType);
    public const string SqlProcedure = nameof(SqlProcedure);
    public const string SqlScalarFunction = nameof(SqlScalarFunction);
    public const string SqlInlineTableValuedFunction = nameof(SqlInlineTableValuedFunction);
    public const string SqlMultiStatementTableValuedFunction = nameof(SqlMultiStatementTableValuedFunction);
    public const string SqlView = nameof(SqlView);
    public const string SqlDmlTrigger = nameof(SqlDmlTrigger);
    public const string SqlSynonym = nameof(SqlSynonym);
    public const string SqlDatabaseOptions = nameof(SqlDatabaseOptions);

    // Properties read.
    public const string BodyScript = nameof(BodyScript);
    public const string QueryScript = nameof(QueryScript);
    public const string CheckExpressionScript = nameof(CheckExpressionScript);
    public const string ExpressionScript = nameof(ExpressionScript);
    public const string DefaultExpressionScript = nameof(DefaultExpressionScript);
    public const string FilterPredicate = nameof(FilterPredicate);
    public const string ForObjectScript = nameof(ForObjectScript);
    public const string IsNullable = nameof(IsNullable);
    public const string IsIdentity = nameof(IsIdentity);
    public const string IsHidden = nameof(IsHidden);
    public const string IsPersistedNullable = nameof(IsPersistedNullable);
    public const string ReturnTableVariable = nameof(ReturnTableVariable);
    public const string DoReturnNullForNullInput = nameof(DoReturnNullForNullInput);
    public const string ClassName = nameof(ClassName);
    public const string MethodName = nameof(MethodName);
    public const string SqlTriggerType = nameof(SqlTriggerType);
    public const string IsInsertTrigger = nameof(IsInsertTrigger);
    public const string IsUpdateTrigger = nameof(IsUpdateTrigger);
    public const string IsDeleteTrigger = nameof(IsDeleteTrigger);
    public const string SqlClrFunctionImplementation = nameof(SqlClrFunctionImplementation);
    public const string GraphType = nameof(GraphType);
    public const string IsNode = nameof(IsNode);
    public const string IsEdge = nameof(IsEdge);
    public const string Collation = nameof(Collation);
    public const string Length = nameof(Length);
    public const string Precision = nameof(Precision);
    public const string Scale = nameof(Scale);
    public const string IsMax = nameof(IsMax);
    public const string IsUnique = nameof(IsUnique);
    public const string IsDisabled = nameof(IsDisabled);
    public const string IsNotForReplication = nameof(IsNotForReplication);
    public const string OnDeleteAction = nameof(OnDeleteAction);
    public const string OnUpdateAction = nameof(OnUpdateAction);
    public const string IsOutput = nameof(IsOutput);
    public const string IsEncrypted = nameof(IsEncrypted);
    public const string IsAnsiNullsOn = nameof(IsAnsiNullsOn);
    public const string IsQuotedIdentifierOn = nameof(IsQuotedIdentifierOn);
    public const string IsAnsiNullDefaultOn = nameof(IsAnsiNullDefaultOn);
    public const string IsAnsiPaddingOn = nameof(IsAnsiPaddingOn);
    public const string IsAnsiWarningsOn = nameof(IsAnsiWarningsOn);
    public const string IsArithAbortOn = nameof(IsArithAbortOn);
    public const string IsConcatNullYieldsNullOn = nameof(IsConcatNullYieldsNullOn);
    public const string IsNumericRoundAbortOn = nameof(IsNumericRoundAbortOn);
    public const string CompatibilityMode = nameof(CompatibilityMode);

    // Relationships read.
    public const string Columns = nameof(Columns);
    public const string ColumnSpecifications = nameof(ColumnSpecifications);
    public const string Column = nameof(Column);
    public const string DefiningTable = nameof(DefiningTable);
    public const string IndexedObject = nameof(IndexedObject);
    public const string ForeignTable = nameof(ForeignTable);
    public const string TypeSpecifier = nameof(TypeSpecifier);
    public const string Type = nameof(Type);
    public const string Parameters = nameof(Parameters);
    public const string FunctionBody = nameof(FunctionBody);
    public const string Parent = nameof(Parent);
    public const string Assembly = nameof(Assembly);

    // Annotations read.
    public const string SysCommentsObjectAnnotation = nameof(SysCommentsObjectAnnotation);
    public const string HeaderContents = nameof(HeaderContents);
    public const string SqlInlineConstraintAnnotation = nameof(SqlInlineConstraintAnnotation);
    public const string IsNotTrustedPropertyAnnotation = nameof(IsNotTrustedPropertyAnnotation);

    // The header's references and their metadata.
    public const string Reference = nameof(Reference);
    public const string SqlSchema = nameof(SqlSchema);
    public const string FileName = nameof(FileName);
    public const string LogicalName = nameof(LogicalName);
    public const string ExternalParts = nameof(ExternalParts);

    /// <summary>A name in <see cref="Namespace"/>.</summary>
    public static XName Name(string local) => Namespace + local;

    /// <summary>The element's type, such as <see cref="SqlTable"/>.</summary>
    public static string TypeOf(XElement element) => (string?)element.Attribute("Type") ?? "";

    /// <summary>The element's own name, such as <c>[sales].[order]</c>; null for one without (an unnamed constraint).</summary>
    public static string? NameOf(XElement element) => (string?)element.Attribute("Name");

    /// <summary>
    /// The name of an element that always has one: a table, a column, a module, a type, a parameter, or what a relationship
    /// refers to. One without is a package this reader doesn't understand, which is a fault, not a diagnostic.
    /// </summary>
    /// <exception cref="FormatException">The element has no name.</exception>
    public static string Named(XElement element) =>
        NameOf(element) ?? throw new FormatException($"A {TypeOf(element)} element of model.xml has no name.");

    /// <summary>A property's value: its <c>Value</c> attribute, or the text of its <c>Value</c> element (a script); null when absent.</summary>
    public static string? Property(XElement element, string name)
    {
        var property = element.Elements(Name("Property")).FirstOrDefault(p => (string?)p.Attribute("Name") == name);
        return property is null ? null : (string?)property.Attribute("Value") ?? property.Element(Name("Value"))?.Value;
    }

    /// <summary>Whether a boolean property is written true; an absent one is false (see the remarks).</summary>
    public static bool IsTrue(XElement element, string name) => Property(element, name) == "True";

    /// <summary>The elements a relationship holds itself, in order: a table's columns, a routine's parameters.</summary>
    public static IEnumerable<XElement> Elements(XElement element, string relationship) =>
        Entries(element, relationship).SelectMany(entry => entry.Elements(Name("Element")));

    /// <summary>The names a relationship refers to, in order: a key's table, a column's type.</summary>
    public static IEnumerable<XElement> References(XElement element, string relationship) =>
        Entries(element, relationship).SelectMany(entry => entry.Elements(Name("References")));

    /// <summary>The first annotation of a kind on the element, or null.</summary>
    public static XElement? Annotation(XElement element, string type) =>
        element.Elements(Name("Annotation")).FirstOrDefault(a => (string?)a.Attribute("Type") == type);

    /// <summary>
    /// The numbers that link an inline constraint to what it was declared on: DacFx gives the constraint an annotation and the
    /// column or table it was written in an annotation or an attached one with the same <c>Disambiguator</c>.
    /// </summary>
    public static IEnumerable<string> Disambiguators(XElement element) =>
        element.Elements()
            .Where(e => e.Name == Name("Annotation") || e.Name == Name("AttachedAnnotation"))
            .Select(e => (string?)e.Attribute("Disambiguator"))
            .OfType<string>();

    private static IEnumerable<XElement> Entries(XElement element, string relationship) =>
        element.Elements(Name("Relationship"))
            .Where(r => (string?)r.Attribute("Name") == relationship)
            .SelectMany(r => r.Elements(Name("Entry")));
}
